//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

/*
    RegisterFile rf1

    [12, 9]  = AB,MACL
 SetDescription("AB,CDIGI","11", "17", @"COUNTER CONTROLLING NUMBER OF DIGITS IN AN OCTAL NUMBER");
            SetDescription("AB,UPPNR","12", "13", @"SCRATCH WORD KEEPING UPPER ADDR LIMIT IN MOPC DUMP CMND");
            SetDescription("AB,CURNR","13", "13", @"SCRATCH WORD KEEPING CURRENT ADDR IN A MOPC DUMP COMMAND");
            SetDescription("AB,CNT10","17", "17", @"COUNTER CONTROLLING NO. OF OCTAL NOS/LINE IN MOPC DUMP CMND");
            SetDescription("AB,PRCHR","16", "16", @"SCRATCH WORD : THE NEXT CHARACTER TO BE WRITTEN BY MOPC");
            SetDescription("AB,TXT1","14", "16", @"SCRATCH WORD : DISPLAY TEXT");
            SetDescription("AB,TXT2","13", "16", @"SCRATCH WORD : DISPLAY TEXT");
            SetDescription("AB,SCRAM","15", "14", @"SCR WD : SCRAMBLED REPRESENTATION OF LETTERS IN MOPC-INPUT");
            SetDescription("AB,OCTNR","15", "15", @"SCRATCH WORD : OCTAL NUMBER ASSEMBLED FROM MOPC-INPUT");
            SetDescription("AB,DISPL","15", "16", @"TYPE OF RUNNING DISPLAY");
            SetDescription("AB,OCTAD","13", "15", @"ADDRESS OF RUNNING DISPLAY");
            SetDescription("AB,OCTA2","12", "16", @"WORD TO EXTEND ADDRESS IN OCTAD TO 24 BITS");
            SetDescription("AB,DEPOS","17", "15", @"SOME OCTAL DIGIT WAS WRITTEN SINCE LAST COMMAND TERMINATED");
            SetDescription("AB,DUMPF","16", "15", @"SCRATCH WORD INDICATING THAT A DUMP IS IN PROGRESS");
            SetDescription("AB,RONLY","14", "15", @"THE EXAMINED REGISTER IS READ-ONLY");
            SetDescription("AB,WRTYP","12", "15", @"TYPE OF VARIABLE IN CASE OF DEPOSIT");
            SetDescription("AB,WRADR","11", "15", @"ADDRESS OF VARIABLE IN CASE OF DEPOSIT");
            SetDescription("AB,ACTLV","17", "14", @"SCRATCH WORD HOLDING 'ACTIVE LEVELS'");
            SetDescription("AB,PVL","15", "17", @"SCRATCH WORD HOLDING 'PREVIOUS LEVEL'");
            SetDescription("AB,IIE","12", "17", @"SCR WORD HOLDING 'PIC' REPRESENTATION OF LAST IIE SETTING");
            SetDescription("AB,PID","14", "17", @"SCR WORD HOLDING MICROPROGRAM-KNOWN BITS OF PID REGISTER");
            SetDescription("AB,PIE","13", "17", @"SCRATCH WORD HOLDING THE 'PIE' REGISTER");
            SetDescription("AB,STATUS","17", "16", @"SCRATCH WORD HOLDING THE LATEST 'COMM, SIOC' INFORMATION");
            SetDescription("AB,OCTN2","15", "13", @"SCRATCH WORD EXPANDING THE 'AB, OCTNA' TO 24 BITS");
            SetDescription("AB,NUMBR","12", "12", @"SCRATCH WORD HOLDING AN OCTAL NUMBER BEING PRINTED BY MOPC");
            SetDescription("AB,OPR","11", "16", @"SCRATCH WORD HOLDING THE 'OPR' REGISTER VALUE");
            SetDescription("AB,BRKPT","16", "14", @"SCRATCH WORD HOLDING BREAKPOINT ADDRESS");
            SetDescription("AB,SINGL","14", "14", @"SCRATCH WORD COUNTING SINGLE-INSTRUCTION");
            SetDescription("AB,BPFLG","13", "14", @"SCRATCH WORD INDICATING THAT BREAKPOINT IS ON");
            SetDescription("AB,MACL","11", "14", @"SCRATCH WORD HOLDING RETRY COUNTER FOR LOAD AFTER MACL");
            SetDescription("AB,LMP","12", "14", @"SCRATCH WORD HOLDING THE 'LMP' REGISTER VALUE");
            SetDescription("AB,EXMOD","11", "13", @"SCRATCH WORD HOLDING THE 'EXM' REGISTER");
            SetDescription("AB,MANIR","14", "13", @"SCRATCH WORD HOLDING FLAG FOR MANUAL IR");
            SetDescription("AB,SSAVE","16", "13", @"SCRATCH WORD HOLDING STS DURING DECIMAL INTRUCTIONS");
            SetDescription("XRF","04", "00", @"SELECT EXTENDED REGISTER FILE");
            SetDescription("AB,UCIL","14", "12", @"SCRATCH WORD HOLDING CACHE INHIBIT UPPER LIMIT");
            SetDescription("AB,LCIL","13", "12", @"SCRATCH REGISTER HOLDING CACHE INHIBIT LOWER LIMIT");
            SetDescription("IDBS,LA", "00", "001600", @"LOGICAL ADDRESS USED BY 'ALU' WHEN JUMP");
            SetDescription("AB,PGS","15", "12", @"COPY OF PGS FROM LAST PF/PV");
            SetDescription("IDBS,INR", "00", "001640", "006000", "00", @"READ INSTALLATION NUMBER");
            SetDescription("AB,STBNK","11", "12", @"GLOBAL BANK POINTER TO THE SEGMENT TABLE BANK");
            SetDescription("AB,STSRT","16", "12", @"GLOBAL POINTER TO THE SEGMENT TABLE WITHIN THE BANK");
            SetDescription("AB,CMBNK","17", "12", @"GLOBAL POINTER TO THE CORE MAP TABLE BANK");
 * 
 */

using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{

    /// <summary>
    /// Register File
    /// 
    /// The register file stores the register blocks for the 16
    /// program levels of the CPU. Each block comprises 16 registers.
    /// Of these, 8 holde the registers P, B, X, L, T, D, A and STS.
    /// 
    /// The other 8 are used by the microprogram as scratch registers,
    /// for the MOPC (Microprogram Operations PanelControl), and the interrupt system.
    /// 
    /// The register file is adressed using the A operand to indicate level, and the
    /// B-Operand to indicate the register within that level.
    /// </summary>
    public class RegisterFile
    {
        const int LEVELS = 0xF;
        const int REGISTER_BLOCK = 0xF;

        string ID = string.Empty;
        ushort[,] registerFile;

        // metadata
        string[,] fieldNames;
        string[,] fieldDescription;

        /// <summary>
        /// Creates a register file with the given number of program levels.
        /// </summary>
        /// <param name="id">
        /// A name for the log.
        /// </param>
        /// <param name="levels">
        /// Number of program levels.
        /// </param>
        public RegisterFile(string id, int levels = LEVELS)
        {
            ID = id;
            registerFile = new ushort[LEVELS + 1, REGISTER_BLOCK + 1];

            fieldNames = new string[LEVELS + 1, REGISTER_BLOCK + 1];
            fieldDescription = new string[LEVELS + 1, REGISTER_BLOCK + 1];
            for (int l = 0; l <= LEVELS; l++)
                for (int r = 0; r <= REGISTER_BLOCK; r++)
                {
                    RegisterEnum re = (RegisterEnum)r;
                    if ((levels == 1) && (l == 0))
                    {
                        // ALU RAM Register 
                        fieldNames[l, r] = re.ToString();
                        fieldDescription[l, r] = $"ALU RAM : {re.ToString()}";
                    }
                    else
                    {
                        // REGISTERFILE 16*16
                        string strRegName = $"Reg #{r}";

                        if ((re > RegisterEnum.Zero) && (re <= RegisterEnum.STS))
                        {
                            strRegName = re.ToString();
                        }

                        fieldNames[l, r] = $"Lvl {l}, {strRegName}";
                        //fieldDescription[l, r] = $"[{id}]:Lvl {l}, {strRegName}";
                    }
                }

        }


        /// <summary>
        /// Writes a register of a program level.
        /// </summary>
        /// <param name="level">
        /// The program level.
        /// </param>
        /// <param name="register">
        /// The register number.
        /// </param>
        /// <param name="value">
        /// The new value.
        /// </param>
        public void Write(ushort level, ushort register, ushort value)
        {
            level &= 0x0F;
            register &= 0x0F;

            Logger.Log($"REGISTER[{ID}].Write {Utils.Int2Oct(value)}=> [{level}, {register}] Name:({fieldNames[level, register]})", Logger.LogLevel.Opcodes);

            registerFile[level, register] = value;
        }


        /// <summary>
        /// Reads the register the A and B operands of the micro word address.
        /// </summary>
        /// <param name="A_operand">
        /// The A operand.
        /// </param>
        /// <param name="B_operand">
        /// The B operand.
        /// </param>
        /// <returns>
        /// The register value.
        /// </returns>
        public ushort Read(ushort A_operand, ushort B_operand)
        {
            A_operand &= 0x0F;
            B_operand &= 0x0F;

            ushort val = registerFile[A_operand, B_operand];
            Logger.Log($"REGISTER[{ID}].Read [{A_operand}, {B_operand}] => {Utils.Int2Oct(val)}  Name:({fieldNames[A_operand, B_operand]})", Logger.LogLevel.Opcodes);
            return val;
        }

        internal ushort Read(int A_operand, int B_operand)
        {
            return Read((ushort)A_operand, (ushort)B_operand);
        }

        /// <summary>
        /// The name of the register the A and B operands address.
        /// </summary>
        /// <param name="A_operand">
        /// The A operand.
        /// </param>
        /// <param name="B_operand">
        /// The B operand.
        /// </param>
        /// <returns>
        /// The name from the token table.
        /// </returns>
        public string GetName(ushort A_operand, ushort B_operand)
        {
            A_operand &= 0x0F;
            B_operand &= 0x0F;


            return fieldNames[A_operand, B_operand];
        }

        /// <summary>
        /// The description of the register the A and B operands address.
        /// </summary>
        /// <param name="A_operand">
        /// The A operand.
        /// </param>
        /// <param name="B_operand">
        /// The B operand.
        /// </param>
        /// <returns>
        /// The description from the token table.
        /// </returns>
        public string GetDescription(ushort A_operand, ushort B_operand)
        {
            A_operand &= 0x0F;
            B_operand &= 0x0F;

            return fieldDescription[A_operand, B_operand];
        }



        /// <summary>
        /// API for setting description, created to match the parameter setup for ND110 tokens
        /// </summary>
        /// <param name="name"></param>
        /// <param name="strB"></param>
        /// <param name="strA"></param>
        /// <param name="description"></param>
        public void SetDescription(string name, string strB, string strA, string description)
        {
            ushort A_operand = (ushort)Utils.Oct2Int(strA);
            ushort B_operand = (ushort)Utils.Oct2Int(strB);

            SetRegDescription(A_operand, B_operand, name, description);
        }

        /// <summary>
        /// Set description to be used for debugger an UI
        /// </summary>
        /// <param name="level">Level</param>
        /// <param name="register">Field offset</param>
        /// <param name="name">Field name / UI label</param>
        /// <param name="description">Field description / UI HelperText</param>
        public void SetRegDescription(ushort level, ushort register, string name, string description)
        {
            level &= 0x0F;
            register &= 0x0F;


            fieldNames[level, register] = name;
            fieldDescription[level, register] = description;
        }



        #region Access to RegisterFile from UI components

        /// <summary>
        /// Writes a register from a user interface, without the micro code side effects.
        /// </summary>
        /// <param name="level">
        /// The program level.
        /// </param>
        /// <param name="register">
        /// The register number.
        /// </param>
        /// <param name="value">
        /// The new value.
        /// </param>
        public void UIWrite(ushort level, ushort register, ushort value)
        {
            level &= 0x0F;
            register &= 0x0F;

            //Utils.Log($"REGISTER[{ID}].Write {Utils.Int2Oct(value)}=> [{level}, {register}] Name:({fieldNames[level, register]})");

            registerFile[level, register] = value;
        }


        /// <summary>
        /// Reads a register for a user interface, without the micro code side effects.
        /// </summary>
        /// <param name="level">
        /// The program level.
        /// </param>
        /// <param name="register">
        /// The register number.
        /// </param>
        /// <returns>
        /// The register value.
        /// </returns>
        public ushort UIRead(ushort level, ushort register)
        {
            level &= 0x0F;
            register &= 0x0F;

            ushort val = registerFile[level, register];
            //Utils.Log($"REGISTER[{ID}].Read [{level}, {register}] => {Utils.Int2Oct(val)}  Name:({fieldNames[level, register]})");
            return val;
        }

        /// <summary>
        /// The name of a register, for a user interface.
        /// </summary>
        /// <param name="level">
        /// The program level.
        /// </param>
        /// <param name="register">
        /// The register number.
        /// </param>
        /// <returns>
        /// The name.
        /// </returns>
        public string UIReadLabel(ushort level, ushort register)
        {
            return fieldNames[level, register];
        }

        /// <summary>
        /// The description of a register, for a user interface.
        /// </summary>
        /// <param name="level">
        /// The program level.
        /// </param>
        /// <param name="register">
        /// The register number.
        /// </param>
        /// <returns>
        /// The description.
        /// </returns>
        public string UIReadDescription(ushort level, ushort register)
        {
            return fieldDescription[level, register];
        }
        #endregion Access to RegisterFile from UI components


        #region Set descrtiptions from ND110 token description

        /// <summary>
        /// Fills the names and descriptions of every register from the ND-110 token descriptions.
        /// </summary>
        public void InitializeMetadata()
        {
            SetDescription("AB,CDIGI", "11", "17", @"COUNTER CONTROLLING NUMBER OF DIGITS IN AN OCTAL NUMBER");
            SetDescription("AB,UPPNR", "12", "13", @"SCRATCH WORD KEEPING UPPER ADDR LIMIT IN MOPC DUMP CMND");
            SetDescription("AB,CURNR", "13", "13", @"SCRATCH WORD KEEPING CURRENT ADDR IN A MOPC DUMP COMMAND");
            SetDescription("AB,CNT10", "17", "17", @"COUNTER CONTROLLING NO. OF OCTAL NOS/LINE IN MOPC DUMP CMND");
            SetDescription("AB,PRCHR", "16", "16", @"SCRATCH WORD : THE NEXT CHARACTER TO BE WRITTEN BY MOPC");
            SetDescription("AB,TXT1", "14", "16", @"SCRATCH WORD : DISPLAY TEXT");
            SetDescription("AB,TXT2", "13", "16", @"SCRATCH WORD : DISPLAY TEXT");
            SetDescription("AB,SCRAM", "15", "14", @"SCR WD : SCRAMBLED REPRESENTATION OF LETTERS IN MOPC-INPUT");
            SetDescription("AB,OCTNR", "15", "15", @"SCRATCH WORD : OCTAL NUMBER ASSEMBLED FROM MOPC-INPUT");
            SetDescription("AB,DISPL", "15", "16", @"TYPE OF RUNNING DISPLAY");
            SetDescription("AB,OCTAD", "13", "15", @"ADDRESS OF RUNNING DISPLAY");
            SetDescription("AB,OCTA2", "12", "16", @"WORD TO EXTEND ADDRESS IN OCTAD TO 24 BITS");
            SetDescription("AB,DEPOS", "17", "15", @"SOME OCTAL DIGIT WAS WRITTEN SINCE LAST COMMAND TERMINATED");
            SetDescription("AB,DUMPF", "16", "15", @"SCRATCH WORD INDICATING THAT A DUMP IS IN PROGRESS");
            SetDescription("AB,RONLY", "14", "15", @"THE EXAMINED REGISTER IS READ-ONLY");
            SetDescription("AB,WRTYP", "12", "15", @"TYPE OF VARIABLE IN CASE OF DEPOSIT");
            SetDescription("AB,WRADR", "11", "15", @"ADDRESS OF VARIABLE IN CASE OF DEPOSIT");
            SetDescription("AB,ACTLV", "17", "14", @"SCRATCH WORD HOLDING 'ACTIVE LEVELS'");
            SetDescription("AB,PVL", "15", "17", @"SCRATCH WORD HOLDING 'PREVIOUS LEVEL'");
            SetDescription("AB,IIE", "12", "17", @"SCR WORD HOLDING 'PIC' REPRESENTATION OF LAST IIE SETTING");
            SetDescription("AB,PID", "14", "17", @"SCR WORD HOLDING MICROPROGRAM-KNOWN BITS OF PID REGISTER");
            SetDescription("AB,PIE", "13", "17", @"SCRATCH WORD HOLDING THE 'PIE' REGISTER");
            SetDescription("AB,STATUS", "17", "16", @"SCRATCH WORD HOLDING THE LATEST 'COMM, SIOC' INFORMATION");
            SetDescription("AB,OCTN2", "15", "13", @"SCRATCH WORD EXPANDING THE 'AB, OCTNA' TO 24 BITS");
            SetDescription("AB,NUMBR", "12", "12", @"SCRATCH WORD HOLDING AN OCTAL NUMBER BEING PRINTED BY MOPC");
            SetDescription("AB,OPR", "11", "16", @"SCRATCH WORD HOLDING THE 'OPR' REGISTER VALUE");
            SetDescription("AB,BRKPT", "16", "14", @"SCRATCH WORD HOLDING BREAKPOINT ADDRESS");
            SetDescription("AB,SINGL", "14", "14", @"SCRATCH WORD COUNTING SINGLE-INSTRUCTION");
            SetDescription("AB,BPFLG", "13", "14", @"SCRATCH WORD INDICATING THAT BREAKPOINT IS ON");
            SetDescription("AB,MACL", "11", "14", @"SCRATCH WORD HOLDING RETRY COUNTER FOR LOAD AFTER MACL");
            SetDescription("AB,LMP", "12", "14", @"SCRATCH WORD HOLDING THE 'LMP' REGISTER VALUE");
            SetDescription("AB,EXMOD", "11", "13", @"SCRATCH WORD HOLDING THE 'EXM' REGISTER");
            SetDescription("AB,MANIR", "14", "13", @"SCRATCH WORD HOLDING FLAG FOR MANUAL IR");
            SetDescription("AB,SSAVE", "16", "13", @"SCRATCH WORD HOLDING STS DURING DECIMAL INTRUCTIONS");
            SetDescription("AB,UCIL", "14", "12", @"SCRATCH WORD HOLDING CACHE INHIBIT UPPER LIMIT");
            SetDescription("AB,LCIL", "13", "12", @"SCRATCH REGISTER HOLDING CACHE INHIBIT LOWER LIMIT");
            SetDescription("AB,PGS", "15", "12", @"COPY OF PGS FROM LAST PF/PV");
            SetDescription("AB,STBNK", "11", "12", @"GLOBAL BANK POINTER TO THE SEGMENT TABLE BANK");
            SetDescription("AB,STSRT", "16", "12", @"GLOBAL POINTER TO THE SEGMENT TABLE WITHIN THE BANK");
            SetDescription("AB,CMBNK", "17", "12", @"GLOBAL POINTER TO THE CORE MAP TABLE BANK");

            // New for ND-120
            SetDescription("AB,BAUD", "10", "01", @"BAUD RATE THUMBWHEEL VALUE (ND-120)");
            SetDescription("AB,OLD303", "15", "05", @"SAVED IOX 303 A REG TO SPOT UART DIFFS (ND-120)");
            SetDescription("AB,NOISE", "10", "02", @"SET ON MACL,RESET ON STOP,INHIBIT LOAD AFTER MACL (ND-120)");

        }

        internal void Clear()
        {
            for (int l = 0; l <= LEVELS; l++)
                for (int r = 0; r <= REGISTER_BLOCK; r++)
                {
                    registerFile[l, r] = 0;
                }
        }
        #endregion
    }
}
