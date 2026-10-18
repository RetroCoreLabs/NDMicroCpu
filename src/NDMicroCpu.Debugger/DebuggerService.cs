//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Collections.Generic;
using System.IO.Ports;
using NDMicroCpu.Core;
using NDMicroCpu.Debugger;
using NDMicroCpu.Debugger.Contracts;

namespace NDMicroCpu.Debugger
{
    class DebuggerService : IDebuggerService
    {
        Machine _machine;

        // OPTIONAL: the 9600-bps serial OPCOM link to a REAL ND-110/CX. Null when no serial port is
        // present (the normal dev-box case). Kept for parity checks against real iron, but it is far
        // too slow for bulk instruction comparison - the emulated microcode path is the default.
        OpComHelper? _opcom;

        bool IsOpcom = false;

        // opcom is optional and defaults to null so the service still constructs (and answers from the
        // emulated microcode CPU) on a machine with no ND-110/CX attached.
        public DebuggerService(Machine machine, OpComHelper? opcom = null)
        {
            _machine = machine;
            _opcom = opcom;

            //StartOpCom();
        }

        public void StartOpCom()
        {
            // Refuse to switch to real hardware when no serial link was opened - otherwise every
            // subsequent Step/Sync would NullReference instead of failing loudly here.
            if (_opcom is null)
            {
                Console.WriteLine("Cannot switch to real ND-110/CX: no OpCom serial link is open.");
                return;
            }

            IsOpcom = true;
        }

        public void StopOpCom()
        {
            IsOpcom = false;
        }


        public ValueTask<StepResponse> Step(StepRequest request)
        {

            if (IsOpcom)
            {
                return OpcomStep(request);
            }


            ushort P = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.P);
            //Console.WriteLine($"Stepping {request.Steps} from address P={Int2Oct(P)}");

            ushort steps = 0;

            // ignore interrupt if already on 13
            int pil = _machine.cpu.hw.alu.PIL;
            if (pil == 0x0d) request.RTCInterrupt = false;

            // Arm the memory trace ONLY around the actual stepping, so that the ReadMemory/WriteMemory
            // requests this service handles (and anything the host does between steps) can never be
            // mistaken for an access the executing instruction made.
            _machine.cpu.memory.TraceMemoryEnabled = request.TraceMemory;
            if (request.TraceMemory) _machine.cpu.memory.ClearMemoryTrace();

            if (request.RTCInterrupt)
            {
                if (_machine.cpu.hw.panel.ForceRTC())
                {
                    _machine.cpu.StepMacroCode(); // trigger the IRQ handler
                    _machine.cpu.StepMacroCode(); // Run the opcode
                }
                else
                    _machine.cpu.StepMacroCode(); // Run the opcode
            }
            else
                _machine.cpu.hw.panel.ClearRTC(); // Make sure we dont have an unexpected RTC interrupt

            for (int i = 0; i < request.Steps; i++)
            {
                if (!_machine.cpu.StepMacroCode()) break; // exit if error
                steps++;
            }

            // Snapshot the trace and disarm before touching anything else.
            List<MemAccess>? memAccesses = null;
            if (request.TraceMemory)
            {
                _machine.cpu.memory.TraceMemoryEnabled = false;

                List<MemoryTraceEntry> recorded = _machine.cpu.memory.MemoryTrace;
                memAccesses = new List<MemAccess>(recorded.Count);
                for (int i = 0; i < recorded.Count; i++)
                {
                    memAccesses.Add(new MemAccess
                    {
                        Address = (int)recorded[i].Address,
                        Value = recorded[i].Value,
                        IsWrite = recorded[i].IsWrite
                    });
                }
            }

            P = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.P, false);

            //Console.WriteLine($"Stopped at address P={Int2Oct(P)}");
            //Console.WriteLine("");


            ushort sts = (ushort)(_machine.cpu.hw.alu.STS | 1 << 12);

            ValueTask<StepResponse> response = new ValueTask<StepResponse>(new StepResponse
            {
                D = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.D, false),
                P = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.P, false),
                B = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.B, false),
                L = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.L, false),
                A = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.A, false),
                T = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.T, false),
                X = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.X, false),
                STS = sts,
                Steps = steps,
                MemAccesses = memAccesses
            });

            return response;
        }

        private ValueTask<StepResponse> OpcomStep(StepRequest request)
        {
            if (_opcom is null) return new ValueTask<StepResponse>(new StepResponse { Steps = 0 });

            ushort P = _opcom.ReadRegister("P");
            //Console.WriteLine($"Stepping {request.Steps} from address P={Int2Oct(P)}");

            ushort steps = 0;

            if (request.RTCInterrupt)
                Console.WriteLine("RTC interrupt - cant force it!!");


            for (int i = 0; i < request.Steps; i++)
            {
                _opcom.SingleStep();
                steps++;
            }

            var allRegs = _opcom.ReadAllRegisters();



            Console.WriteLine($"Stopped at address P={Int2Oct(allRegs.P)}");
            Console.WriteLine("");


            ushort sts = (ushort)(_machine.cpu.hw.alu.STS | 1 << 12);

            ValueTask<StepResponse> response = new ValueTask<StepResponse>(new StepResponse
            {
                D = allRegs.D,
                P = allRegs.P,
                B = allRegs.B,
                L = allRegs.L,
                A = allRegs.A,
                T = allRegs.T,
                X = allRegs.X,
                STS = allRegs.STS,
                Steps = steps
            });

            return response;
        }

        public ValueTask<SyncResponse> Sync(SyncRequest request)
        {
            Console.WriteLine($"SYNC request! P = {Int2Oct(request.P)}");

            if (IsOpcom)
            {
                return OpcomSync(request);
            }

            _machine.cpu.WriteALURegister(NDMicroCpu.Core.Enums.RegisterEnum.D, request.D);
            _machine.cpu.WriteALURegister(NDMicroCpu.Core.Enums.RegisterEnum.P, request.P);
            _machine.cpu.WriteALURegister(NDMicroCpu.Core.Enums.RegisterEnum.B, request.B);
            _machine.cpu.WriteALURegister(NDMicroCpu.Core.Enums.RegisterEnum.L, request.L);
            _machine.cpu.WriteALURegister(NDMicroCpu.Core.Enums.RegisterEnum.A, request.A);
            _machine.cpu.WriteALURegister(NDMicroCpu.Core.Enums.RegisterEnum.T, request.T);
            _machine.cpu.WriteALURegister(NDMicroCpu.Core.Enums.RegisterEnum.X, request.X);

            //_machine.cpu.WriteALURegister(NDMicroCpu.Core.Enums.RegisterEnum.STS, request.STS);
            _machine.cpu.hw.alu.LoadSTS(request.STS);

            ValueTask<SyncResponse> response = new ValueTask<SyncResponse>(new SyncResponse
            {
                P = _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.P)
            });

            return response;
        }

        private ValueTask<SyncResponse> OpcomSync(SyncRequest request)
        {

            ND_Registers regs = new ND_Registers();

            regs.D = request.D;
            regs.P = request.P;
            regs.B = request.B;
            regs.L = request.L;
            regs.A = request.A;
            regs.T = request.T;
            regs.X = request.X;
            regs.STS = request.STS;

            _opcom?.WriteAllRegisters(regs);

            ValueTask<SyncResponse> response = new ValueTask<SyncResponse>(new SyncResponse
            {
                P = _opcom != null ? _opcom.ReadRegister("P") : _machine.cpu.ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.P)
            });

            return response;
        }

        public string Int2Oct(int val)
        {
            return Convert.ToString(val, 8).PadLeft(6, '0');
        }

        public ValueTask<ReadMemResponse> ReadMemory(ReadMemRequest request)
        {
            Console.Write($"ReadMem request! P = {Int2Oct(request.Address)}");

            ushort data = 0;

            if (IsOpcom && _opcom != null)
                data = _opcom.ReadMemory((ushort)request.Address);
            else
                data = _machine.cpu.memory.ReadPhysicalMemory(request.Address);

            ValueTask<ReadMemResponse> response = new ValueTask<ReadMemResponse>(new ReadMemResponse
            {
                Data = data


            });

            Console.WriteLine($", Value= {Int2Oct(data)}");

            return response;

        }


        public ValueTask<WriteMemResponse> WriteMemory(WriteMemRequest request)
        {
            Console.WriteLine($"WriteMem request! P = {Int2Oct(request.Address)}, Value = {Int2Oct(request.Value)}");

            if (IsOpcom)
            {
                return OpcomWriteMemory(request);
            }

            ValueTask<WriteMemResponse> response = new ValueTask<WriteMemResponse>(new WriteMemResponse
            {
                WriteSucceeded = true
            });

            try
            {
                if (request.PhysicalMem)
                {
                    _machine.cpu.memory.WritePhysicalMemory(request.Address, request.Value);
                }
                else
                {
                    //TODO: Set pagetable ?
                    _machine.cpu.memory.WritePhysicalMemory(request.Address, request.Value);
                }
            }
            catch (Exception)
            {
                response = new ValueTask<WriteMemResponse>(new WriteMemResponse
                {
                    WriteSucceeded = false
                });
                Console.WriteLine($"WriteMem FAILED!");
            }

            return response;


        }

        public ValueTask<ClearMemResponse> ClearMemory(ClearMemRequest request)
        {
            // Zero the oracle RAM so a per-opcode comparison test starts from the same clean image as
            // RetroCore's fresh-per-test machine. Only the ram[] array is cleared; page tables (shadow
            // storage) are left intact. Not applicable to a real ND-110 over the OpCom serial link.
            if (IsOpcom)
            {
                Console.WriteLine("ClearMemory ignored: OpCom (real ND-110) memory cannot be bulk-cleared.");
                return new ValueTask<ClearMemResponse>(new ClearMemResponse { Cleared = false });
            }

            _machine.cpu.memory.ClearRAM(request.Start, request.Count);
            return new ValueTask<ClearMemResponse>(new ClearMemResponse { Cleared = true });
        }

        private ValueTask<WriteMemResponse> OpcomWriteMemory(WriteMemRequest request)
        {
            ValueTask<WriteMemResponse> response = new ValueTask<WriteMemResponse>(new WriteMemResponse
            {
                WriteSucceeded = true
            });

            try
            {
                //if (request.PhysicalMem)
                {
                    if (_opcom != null) _opcom.WriteMemory((ushort)request.Address, request.Value);
                }

            }
            catch (Exception)
            {
                response = new ValueTask<WriteMemResponse>(new WriteMemResponse
                {
                    WriteSucceeded = false
                });
                Console.WriteLine($"WriteMem FAILED!");
            }

            return response;
        }
    }
}
