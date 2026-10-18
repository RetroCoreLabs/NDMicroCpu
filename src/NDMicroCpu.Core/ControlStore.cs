//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Tasks;

namespace NDMicroCpu.Core
{


    /// <summary>
    /// Contains the 64KW with MicroCode for the ND-110 CPU
    /// </summary>
    public class ControlStore
    {
        ulong[] microcode;

        /// <summary>
        /// Creates an empty 64K-word control store.
        /// </summary>
        public ControlStore()
        {
            microcode = new ulong[1024 * 64];
        }

        /// <summary>
        /// Reads one micro word.
        /// </summary>
        /// <param name="address">
        /// The control store address.
        /// </param>
        /// <returns>
        /// The 64-bit micro word.
        /// </returns>
        public ulong Read(ushort address)
        {
            return microcode[address];
        }

        /// <summary>
        /// Writes one micro word.
        /// </summary>
        /// <param name="address">
        /// The control store address.
        /// </param>
        /// <param name="value">
        /// The 64-bit micro word.
        /// </param>
        public void Write(ushort address, ulong value)
        {
            microcode[address] = value;
        }


        /// <summary>
        /// Clears the whole control store to zero.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < microcode.Length; i++)
            {
                microcode[i] = 0;
            }
        }


        /// <summary>
        /// Loads a .CODE image written by the NDMicroAsm assembler.
        /// </summary>
        /// <param name="fileName">
        /// Path of the image.
        /// </param>
        /// <param name="isND110">
        /// True for the ND-110 word packing, false for ND-120.
        /// </param>
        /// <returns>
        /// True when the file was read.
        /// </returns>
        public bool LoadFromFile(string fileName, bool isND110)
        {
            try
            {
                if (!File.Exists(fileName)) return false;
                byte[] data = File.ReadAllBytes(fileName);
                int records = LoadFromByteArray(data, isND110);

                Console.WriteLine($"Loaded Control Store, length= 0x{records:X4}");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.ToString()}");
                return false;
            }
        }

        /// <summary>
        /// Loads a .CODE image from memory.
        /// </summary>
        /// <param name="data">
        /// The image bytes.
        /// </param>
        /// <param name="isND110">
        /// True for the ND-110 word packing, false for ND-120.
        /// </param>
        /// <returns>
        /// Number of micro words loaded.
        /// </returns>
        public int LoadFromByteArray(byte[] data, bool isND110)
        {
            // Clear existing Control Store
            Clear();

            int records = data.Length / 10; // 10 bytes pr microcode
            for (int i = 0; i < data.Length;)
            {
                // Read address
                ushort address = data[i++];
                address |= (ushort)(data[i++] << 8);

                // Read microcode
                ulong uc = 0;
                byte byteval = 0;
                for (int j = 0; j < 8; j++)
                {
                    byteval = data[i++];
                    ulong tmp = byteval;
                    uc |= (ulong)(tmp << (j * 8));

                    //Console.WriteLine($"Loading CS [{j}] b:{byteval:X2} = {uc:X16}");
                }

                if (isND110)
                {
                    // Invert bits 21-27 so they are active HIGH (like ND-120)
                    // Makes decoding the bits much easier.
                    uc = uc ^ 0x000000000FC00000; // Invert bit 22-27
                }

                microcode[address] = uc;

                //Console.WriteLine($"Loading CS, address {Numeric.Int2Oct(address)} = {uc:X16}");
            }

            return records;
        }

        /// <summary>
        /// Writes the control store as a .CODE image.
        /// </summary>
        /// <param name="fileName">
        /// Path of the image to write.
        /// </param>
        /// <returns>
        /// True when the file was written.
        /// </returns>
        public bool SaveControlStoreToFile(string fileName)
        {
            try
            {
                using (var fileStream = new FileStream(fileName, FileMode.Create))
                {
                    for (ushort i = 0; i < microcode.Length; i++)
                    {

                        // First 2 bytes is the 16 bits address
                        fileStream.WriteByte((byte)(i & 0xFF));
                        fileStream.WriteByte((byte)((i >> 8) & 0xFF));

                        ulong uC = microcode[i];


                        // Write microcode
                        for (int j = 0; j < 8; i++)
                        {

                            byte byteval = (byte)(uC & 0xFF);
                            uC = uC >> 8;

                            fileStream.WriteByte(byteval);
                        }
                    }
                }
                Utils.Log($"Saved Control Store to file {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                Utils.Log($"Error saving Control Store to file {fileName}: {ex.Message}");
                return false;
            }
        }



        /// <summary>
        /// Read 16-bits from CS at position 'pos'
        /// </summary>
        /// <param name="pos">
        /// The 16-bit part position.
        /// </param>
        /// <returns>
        /// The 16 bits.
        /// </returns>
        internal ushort ReadCS(ushort pos)
        {
            int address = 0;

            if (pos > 0)
                address = pos / 4;


            ulong uc = microcode[address];
            int shift = pos & 0x03;
            uc = uc >> (pos * 16);

            return (ushort)uc;

        }
    }
}
