//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.IO.Ports;
using NDMicroCpu.Debugger;
using ProtoBuf.Grpc.Server;

// ndmicrodebug - a gRPC server in front of the micro code CPU, so another emulator can
// step this one instruction by instruction and compare registers and memory (the
// contract is NDMicroCpu.Debugger.Contracts.IDebuggerService; code-first gRPC, see
// https://learn.microsoft.com/en-us/aspnet/core/grpc/code-first).

Machine machine = new Machine();
machine.cpu.DisableInternalRTC();

Console.Write("Booting ND-110 micro code startup...");
machine.Initialize();
Console.WriteLine("..done");

const int port = 9000;

// OpCom is the OPTIONAL serial link to a REAL ND-110/CX (toggled at runtime with F1/F2).
// It must never gate the emulated path: DebuggerService.IsOpcom defaults to false, so with
// no serial port present the service still answers Sync/Step/ReadMemory/WriteMemory from the
// micro code emulator. Opening a non-existent port used to throw here and kill the process
// BEFORE Kestrel started listening, making the server look "never built" to gRPC clients.
OpComHelper? opcom = null;
const string opcomPortName = "COM11";
try
{
    SerialPort serialPort = new SerialPort(opcomPortName, 9600, Parity.Even, 7);
    serialPort.Open();
    opcom = new OpComHelper(serialPort);
    Console.WriteLine($"OpCom serial link opened on {opcomPortName} (F1 = switch to real ND-110/CX)");
}
catch (Exception ex)
{
    // Missing/busy port is expected on a dev box with no real ND-110 attached.
    Console.WriteLine($"OpCom serial link on {opcomPortName} unavailable ({ex.GetType().Name}: {ex.Message}).");
    Console.WriteLine("Continuing with the EMULATED micro code CPU only - F1 (real hardware) will be inactive.");
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    // gRPC needs HTTP/2.
    options.ListenAnyIP(port, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter(level => level >= LogLevel.Warning);
builder.Services.AddCodeFirstGrpc();
builder.Services.AddSingleton(machine);
// Only register OpComHelper when a serial link actually opened. When absent, the
// DebuggerService ctor's default (null) is used by ActivatorUtilities instead.
if (opcom is not null) builder.Services.AddSingleton(opcom);

WebApplication app = builder.Build();
app.MapGrpcService<DebuggerService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
Console.WriteLine($"Listening on port {port}");

// Keyboard: PageUp/PageDown toggle the micro code log, Home clears the screen, any other key
// goes to the emulated console (CTRL-A..Z become 0x01..0x1A).
Task keyboardInputTask = Task.Run(() =>
{
    while (true)
    {
        if (Console.KeyAvailable)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.PageUp:
                    machine.SetLog(true);
                    break;

                case ConsoleKey.PageDown:
                    machine.SetLog(false);
                    break;

                case ConsoleKey.Home:
                    Console.Clear();
                    break;

                case ConsoleKey.F1:
                    Console.WriteLine("Switching to real-world ND110/CX");
                    //debuggerService?.StartOpCom();
                    break;

                case ConsoleKey.F2:
                    Console.WriteLine("Switching from real-world ND110/CX to emulated");
                    //debuggerService?.StopOpCom();
                    break;

                default:
                    byte myKey = (byte)char.ToUpper(key.KeyChar);
                    if ((key.Modifiers == ConsoleModifiers.Control) && ((myKey >= 65) && (myKey <= 90)))
                    {
                        myKey = (byte)(myKey - 64);  // Make CTRL-A to 0x01, CTRL-B to 0x02..
                    }
                    if (myKey != 0)
                    {
                        machine.cpu.hw.AddConsoleChar((char)myKey);
                    }
                    break;
            }
        }
    }
});

await app.RunAsync();
