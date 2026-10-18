//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

/*
 * 
 * This class converts a general way to do Floating Point operations to and from binary values
 * 
 * The IEEE 754 Standard is available here:
 * https://en.wikipedia.org/wiki/IEEE_754
 * 
 * To learn how this really work, I recommend theese videos:
 * 
 *      IEEE 754 Standard for Floating Point Binary Arithmetic
 *      https://www.youtube.com/watch?v=RuKkePyo9zk&ab_channel=ComputerScience
 * 
 *      Binary 1 - Converting to and from Denary
 *      https://www.youtube.com/watch?v=cJNm938Xwao
 *      
 *      Binary 2 - Two's Complement Representation of Negative Numbers
 *      https://www.youtube.com/watch?v=mRvcGijXI9w
 *      
 *      Binary 3 – Fixed Point Binary Fractions
 *      https://www.youtube.com/watch?v=QFlbvSeBkwY
 *      
 *      Binary 4 – Floating Point Binary Fractions 1
 *      https://www.youtube.com/watch?v=L8OYx1I8qNg
 *      
 *      Binary 5 – Floating Point Range versus Precision
 *      https://www.youtube.com/watch?v=A2HflP5sa_0&ab_channel=ComputerScience
 *      
 *      Binary 6 – Normalised Floating Point Binary Fractions
 *      https://www.youtube.com/watch?v=otjHbynpxNs&ab_channel=ComputerScience
 *      
 *      Binary 7 – Floating Point Binary Addition
 *      https://www.youtube.com/watch?v=aH11flclJDI
 *      
 *      Binary 8 – Floating Point Binary Subtraction
 *      https://www.youtube.com/watch?v=JnorFLL86DE
 *      
 *      
 * See also : https://jonskeet.uk/csharp/DoubleConverter.cs
 * 
 * Look into Berkley Softfloat: http://www.jhauser.us/arithmetic/SoftFloat.html https://github.com/ucb-bar/berkeley-softfloat-3
 *  
*/
namespace NDMicroCpu.Core.Common
{

    /// <summary>
    /// What went wrong in a floating point conversion.
    /// </summary>
    public enum FPPError
    {
        /// <summary>
        /// The value converted without loss.
        /// </summary>
        NO_ERROR,
        /// <summary>
        /// The value was too small for the exponent range and became zero.
        /// </summary>
        UNDERFLOW,
        /// <summary>
        /// The value was too large for the exponent range.
        /// </summary>
        OVERFLOW,
    }

    /// <summary>
    /// A software floating point number in one of the formats the ND machines and their contemporaries used, with conversion to and from a host double.
    /// </summary>
    /// <remarks>
    /// The format is described by its exponent length, mantissa length, exponent bias and whether the leading one of the mantissa is hidden; see the comment at the top of this file for the ND 32-bit and 48-bit layouts.
    /// </remarks>
    public class FPP
    {

        internal FPPType fpp_type { get; }
        FPPError Error { get; set; }
        /// <summary>
        /// Number of exponent bits.
        /// </summary>
        public int ExponentLength { get; }
        /// <summary>
        /// Number of stored mantissa bits (without the hidden bit).
        /// </summary>
        public int MantissaLength { get; }
        /// <summary>
        /// The value subtracted from the stored exponent to get the real one.
        /// </summary>
        public int Bias { get; }
        /// <summary>
        /// True when the leading one of the mantissa is not stored (IEEE 754 style).
        /// </summary>
        public bool HiddenBit { get; }


        /// <summary>
        /// The mantissa as stored, right aligned.
        /// </summary>
        public long Mantissa { get; set; }
        /// <summary>
        /// The exponent with the bias removed.
        /// </summary>
        public long Exponent { get; set; }

        /// <summary>
        /// True when the number is negative.
        /// </summary>
        public bool Negative { get; set; }

        internal bool IsZero;
        /*
         *
         * 1 - 8 -23
         *
            IEEE 754 single-precision binary floating-point format
            ======================================================
            https://en.wikipedia.org/wiki/Single-precision_floating-point_format

            Visualizer tool: https://float-visualizer.surge.sh/
            Single-precision floating-point format (sometimes called FP32 or float32)
            The data format of floating point words uses 24 bits (23 bit explicitly stored) for the mantissa, one bit for sign and 8 bits for *biased* exponent.


            The IEEE 754 standard specifies a binary32 as having:

                +------+-----------------+-------------------------------+
                | 31	| 30 -        23 | 22          -              0  |
                +------+-----------------+-------------------------------+
                | Sign | Exponent 8-bit  | fraction/mantissa  23 bit     |
                +------+-----------------+-------------------------------+
                The exponent is biassed with 127 


                Sign bit: 1 bit
                Exponent width: 8 bits
                Significand precision: 24 bits (23 explicitly stored)


            IEEE 754 double-precision binary floating-point format: binary64
            =================================================================
            https://en.wikipedia.org/wiki/Double-precision_floating-point_format

            Double-precision binary floating-point is a commonly used format on PCs, due to its wider range over single-precision floating point, 
            in spite of its performance and bandwidth cost. 

            It is commonly known simply as double. The IEEE 754 standard specifies a binary64 as having:


                +------+-----------------+-------------------------------+
                | 63   | 62 -         52 | 51          -              0  |
                +------+-----------------+-------------------------------+
                | Sign | Exponent 11-bit | fraction/mantissa  52 bit     |
                +------+-----------------+-------------------------------+
                The exponent is biassed with 2^10 -1 (1023)


                Sign bit: 1 bit
                Exponent: 11 bits
                Significand precision: 53 bits (52 explicitly stored)


            ND-100 32 BIT FPP
            =================

            The data format of floating point words uses 22 bits for the mantissa, one bit for sign and 9 bits for *biased* exponent.

                +------+-----------------+-------------------------------+
                | 31   | 30 -         22 | 21          -              0  |
                +------+-----------------+-------------------------------+
                | Sign | Exponent 9-bit  | fraction/mantissa  22 bit     |
                +------+-----------------+-------------------------------+
                The exponent is biassed with 2^8 (256)

                range			: 10 ^-76 < x < 10 ^76
                accuracy		: 23 bits (approximately 7 decimal places)
                floating zero	: 0 in all 32 bits

                 Example:
                                octal
                    +-----------+-----------+-----------+
                    |integer	| A word	|  D word   |
                    +-----------+-----------+-----------+
                    |0			|  000000	| 000000	|
                    +-----------+-----------+-----------+
                    |+1			|  040100	| 000000	|
                    +-----------+-----------+-----------+
                    |-1			|  140100	| 000000	|
                    +-----------+-----------+-----------+
                    |+3			|  040240	| 000000	|
                    +-----------+-----------+-----------+


            ND-100 48 bit floating point
            ============================

            The data format of floating point words uses 32 bits for the mantissa, one bit for sign and 15 bits for *biased* exponent.

                +------+-----------------+---------------+---------------+
                | 47	| 46 -         32 | 31         16 |  15   -    0  |
                +------+-----------------+---------------+---------------+
                | Sign | Exponent 15-bit | fraction/mantissa  22 bit     |
                +------+-----------------+---------------+---------------+

                The exponent is biased with 2^14 (16384)

                Range			: 10 ^-4920 < x < 10  ^4920
                Accuracy		: 32 bits (approximately 10 decimal digits)
                Floating Zero	: O in all bits


                Examples

                                octal
                +-----------+-----------+-----------+-----------+
                |integer	| T word	|  A word   |  D word   |
                +-----------+-----------+-----------+-----------+
                |0			|  000000	| 000000	| 000000	|
                +-----------+-----------+-----------+-----------+
                |+1			|  040001	| 100000	| 000000	|
                +-----------+-----------+-----------+-----------+
                |-1			|  140001	| 100000	| 000000	|
                +-----------+-----------+-----------+-----------+
                |+3			|  040240	| 100000	| 000000	|
                +-----------+-----------+-----------+-----------+


            PDP11 2-Words (32 bits)
            ========================
            https://nssdc.gsfc.nasa.gov/nssdc/formats/PDP-11.htm

            Floating point numbers for PDP computers are stored in either 2 words (totaling 32 bits) or 4 words (totaling 64 bits). 
            The normalized bit is assumed to be 1 unless the exponent is 0 (corresponding to 2 to the 128) in which case that bit is assumed to be 0.  
            The value 0 is represented by two (or four) words of all zeros.



                +------+-----------------+-------------------------------+
                | 31   | 30 -         23 | 22          -              0  |
                +------+-----------------+-------------------------------+
                | Sign | Exponent 8-bit  | fraction/mantissa  23 bit     |
                +------+-----------------+-------------------------------+
                The exponent is biased with 127


                Sign bit: 1 bit
                Exponent: 8 bits
                Significand precision: 24 bits (23 explicitly stored)

            For instance, 2.0 is 40200 in octal or 0100000010000000 in binary (remember the 128 bias and the hidden bit), plus a following 16-bit word of all zeros.

            PDP11 4-Words (64 bits)
            ========================
            https://nssdc.gsfc.nasa.gov/nssdc/formats/PDP-11.htm

            Floating point numbers for PDP computers are stored in either 2 words (totaling 32 bits) or 4 words (totaling 64 bits). 
            The normalized bit is assumed to be 1 unless the exponent is 0 (corresponding to 2 to the 128) in which case that bit is assumed to be 0.  
            The value 0 is represented by two (or four) words of all zeros.



            The 4 word (64Bits) is similar to the 2 word, execpt it adds 32 bits more to the mantissa (16+16)
            Four word floating point numbers add two more words for the fraction of decreasing significance and increasing precision.

                +------+-----------------+-------------------------------+
                | 31   | 30 -         58 | 54          -              0  |
                +------+-----------------+-------------------------------+
                | Sign | Exponent 8-bit  | fraction/mantissa  55 bit     |
                +------+-----------------+-------------------------------+
                The exponent is biased with 127


                Sign bit: 1 bit
                Exponent: 8 bits
                Significand precision: 56 bits (55 explicitly stored)

            For instance, 2.0 is 40200 in octal or 0100000010000000 in binary (remember the 128 bias and the hidden bit), plus a following 16-bit word of all zeros.



         */

        /// <summary>
        /// The floating point formats the class knows the layout of.
        /// </summary>
        public enum FPPType
        {
            /// <summary>
            /// Layout given explicitly through the four-argument constructor.
            /// </summary>
            UNKNOWN,
            /// <summary>
            /// IEEE 754 single precision: 8-bit exponent, 23-bit mantissa, hidden bit.
            /// </summary>
            IEEE754_SinglePrecision,
            /// <summary>
            /// IEEE 754 double precision: 11-bit exponent, 52-bit mantissa, hidden bit.
            /// </summary>
            IEEE754_DoublePrecision,
            /// <summary>
            /// The ND-100 32-bit floating point format: 9-bit exponent, 22-bit mantissa.
            /// </summary>
            ND32Bit,
            /// <summary>
            /// The ND-100 48-bit floating point format: 16-bit exponent, 31-bit mantissa.
            /// </summary>
            ND48Bit,
            /// <summary>
            /// The PDP-11 32-bit floating point format.
            /// </summary>
            PDP32Bit,
            /// <summary>
            /// The PDP-11 64-bit floating point format.
            /// </summary>
            PDP64Bit
        }

        /// <summary>
        /// Creates a number in one of the known formats.
        /// </summary>
        /// <param name="type">
        /// The format; its layout comes from the table in the constructor.
        /// </param>
        public FPP(FPPType type)
        {
            fpp_type = type;

            switch (type)
            {
                case FPPType.IEEE754_SinglePrecision:
                    ExponentLength = 8;
                    MantissaLength = 23;
                    Bias = 127;
                    HiddenBit = false;
                    break;

                case FPPType.IEEE754_DoublePrecision:
                    ExponentLength = 11;
                    MantissaLength = 52;
                    Bias = 1023;
                    HiddenBit = false;
                    break;

                case FPPType.ND32Bit:
                    ExponentLength = 9;
                    MantissaLength = 22;
                    Bias = 257;
                    HiddenBit = true;
                    break;

                case FPPType.ND48Bit:
                    ExponentLength = 15;
                    MantissaLength = 32;
                    Bias = 1024;
                    HiddenBit = true;
                    break;

                case FPPType.PDP32Bit:
                    ExponentLength = 8;
                    MantissaLength = 23;
                    Bias = 128;
                    HiddenBit = true;
                    break;
                case FPPType.PDP64Bit:
                    ExponentLength = 8;
                    MantissaLength = 55; // 23 +32
                    Bias = 127;
                    HiddenBit = true;
                    break;
                default:
                    throw new Exception("Unknown FPP Type");
            }
        }

        /// <summary>
        /// Creates a number in a format given by its layout.
        /// </summary>
        /// <param name="exponentLength">
        /// Number of exponent bits.
        /// </param>
        /// <param name="mantissaLength">
        /// Number of stored mantissa bits.
        /// </param>
        /// <param name="bias">
        /// The exponent bias.
        /// </param>
        /// <param name="hiddenBit">
        /// True when the leading one of the mantissa is not stored.
        /// </param>
        public FPP(int exponentLength, int mantissaLength, int bias, bool hiddenBit)
        {
            fpp_type = FPPType.UNKNOWN;
            ExponentLength = exponentLength;
            MantissaLength = mantissaLength;
            Bias = bias;
            HiddenBit = hiddenBit;

            Error = FPPError.NO_ERROR;
        }


        void SetZero()
        {
            Mantissa = 0;
            Exponent = 0;
            Negative = false;
            IsZero = true;
        }

        /// <summary>
        /// Loads the number from its bit pattern in the format of this instance.
        /// </summary>
        /// <param name="bits">
        /// Sign, exponent and mantissa packed as the format stores them, right aligned.
        /// </param>
        public void FromLong(long bits)
        {
            int totalBitLen = ExponentLength + MantissaLength + 1; // +1 because first bit is always the negative bit.

            long mantissaMask = (long)(Math.Pow(2, MantissaLength) - 1);
            long exponentMask = (long)(Math.Pow(2, ExponentLength) - 1);


            if (bits == 0)
            {
                SetZero();
                return;
            }
            IsZero = false;

            // Save the Mantissa Bits
            Mantissa = (bits & mantissaMask);

            // Determine the sign in denary
            Negative = ((bits >> (MantissaLength + ExponentLength)) & 0x01) != 0;

            // Determine the exponent in Denery and remove the exponent bias
            Exponent = ((bits >> MantissaLength) & exponentMask) - Bias;
        }

        /// <summary>
        /// Packs the number into its bit pattern in the format of this instance.
        /// </summary>
        /// <returns>
        /// Sign, exponent and mantissa packed as the format stores them, right aligned.
        /// </returns>
        public long ToLong()
        {

            if (IsZero) return 0;

            long res = 0;
            int totalBitLen = ExponentLength + MantissaLength + 1; // +1 because first bit is always the negative bit.

            long mantissaMask = (long)(Math.Pow(2, MantissaLength) - 1);
            long exponentMask = (long)(Math.Pow(2, ExponentLength) - 1);
            long allMask = (long)(Math.Pow(2, totalBitLen) - 1);

            long m = Mantissa;

            // Add MANTISSA
            res |= (m & mantissaMask);

            // Add EXPONENT
            res |= (long)(((Exponent + Bias) & exponentMask) << MantissaLength);

            // Add SIGN
            if (Negative)
                res |= (long)(1 << (totalBitLen - 1));

            // Make sure there are no extra bits
            res &= allMask;


            return res;
        }


        /// <summary>
        /// Sets the number from a host double, rounding to the mantissa length of the format.
        /// </summary>
        /// <param name="val">
        /// The value to convert.
        /// </param>
        public void FromDouble(double val)
        {

            if (val == 0.0)
            {
                SetZero();
                return;
            }

            IsZero = false;

            long bits = BitConverter.DoubleToInt64Bits(val);


            // Note that the shift is sign-extended, hence the test against -1 not 1
            Negative = (bits & (1L << 63)) != 0;
            Exponent = (int)((bits >> 52) & 0x7ffL);
            Exponent -= 1023; // Exponent for Double() is biased with 1023. Internal "Exponend" has no Bias. Re-Adds correct Bias when going via ToLong() and ToDouble()
            Mantissa = bits & 0xfffffffffffffL;


            int bitMove;

            // Now update the class internal variables for Exponent and Mantissa to match FPP definition.           // 
            // Double - exponent is 11 bits
            // Double - mantissa is 52 bits
            // Now update the data to the internal format

            //// Exponent
            //if (ExponentLength >= 11)
            //{
            //    // Exponent in FPP has more bits then exponent in Double()
            //    bitMove = ExponentLength - 11;
            //    Exponent  = Exponent << bitMove;
            //}
            //else
            //{
            //    // Exponent in FPP has less bits then exponent in Double()
            //    bitMove = 11 - ExponentLength;
            //    Exponent = Exponent >> bitMove;
            //}

            // Mantissa
            if (MantissaLength >= 52)
            {
                // Mantissa in FPP has more bits then mantissa in Double()
                bitMove = MantissaLength - 52;
                Mantissa = Mantissa << bitMove;
            }
            else
            {
                // Mantissa in FPP has less bits then mantissa in Double()
                bitMove = 52 - MantissaLength;
                Mantissa = Mantissa >> bitMove;
            }

        }



        /// <summary>
        /// Converts the number to a host double.
        /// </summary>
        /// <returns>
        /// The nearest double; the ND 48-bit mantissa is wider than a double and loses its low bits.
        /// </returns>
        public double ToDouble()
        {

            if (IsZero) return 0.0;

            // Update frpm the class internal variables for Exponent and Mantissa to match FPP definition
            // Double - exponent is 11 bits
            // Double - mantissa is 52 bits

            int bitMove;

            long E = Exponent;
            long M = Mantissa;

            // Exponent
            //if (ExponentLength >= 11)
            //{
            //    bitMove = ExponentLength - 11;
            //    E = E >> bitMove;
            //}
            //else
            //{
            //    bitMove = 11 - ExponentLength;
            //    E = E << bitMove;
            //}

            //Double() Exponent is Biased with 1023 - Add this here
            E += 1023;

            // Mantissa
            if (MantissaLength >= 52)
            {
                bitMove = MantissaLength - 52;
                M = M >> bitMove;
            }
            else
            {
                bitMove = 52 - MantissaLength;
                M = M << bitMove;
            }


            long ieee = 0;
            if (Negative)
                ieee |= (1L << 63);
            ieee |= (E & 0x7ffL) << 52;
            ieee |= M & 0xfffffffffffffL;

            return BitConverter.Int64BitsToDouble(ieee);
        }


        internal double Mantissa2Double()
        {
            int mLen;
            double decimalTotal, decimalPart;
            mLen = MantissaLength;
            long man = Mantissa;
            if (HiddenBit)
            {
                mLen++;
                man |= 1L << (mLen - 1);
            }

            decimalTotal = 0;
            for (int i = -1; i >= -MantissaLength; i--)
            {
                decimalPart = ((man >> (mLen - 1)) & 0x01) * Math.Pow(2, i);
                decimalTotal += decimalPart;
                mLen--;
            }
            return decimalTotal;
        }

        internal long Double2Mantissa(double value)
        {
            int mLen;
            double k, remaining;
            long res = 0;
            mLen = MantissaLength - 1;
            if (HiddenBit)
            {
                //   mLen++;
            }

            remaining = 0; k = 0;
            for (int i = -1; i >= -MantissaLength; i--)
            {
                remaining = k + Math.Pow(2, i);
                if (remaining > value)
                {
                    res &= ~(1 << mLen); // Put in a ZERO at this position
                }
                else
                {
                    res |= (long)(1 << mLen); // Put in a ONE at this position
                    k = remaining;
                }
                mLen--;
            }
            return res;
        }

        /// <summary>
        /// Helper funcition to initalize FPP using 3x 16 bits words for 32 or 48 bits FPP
        /// </summary>
        /// <param name="bitsHi"></param>
        /// <param name="bitsMedium"></param>
        /// <param name="bitsLo"></param>
        public void From3Words(ushort bitsHi, ushort bitsMedium, ushort bitsLo)
        {
            long l = (long)bitsHi << 32;
            l |= (long)bitsMedium << 16;
            l |= (long)bitsLo;
            FromLong(l);
        }

        /// <summary>
        /// Converts the 'denary' number to a standard form floating number in the floating accumulator, using 'scaling' as a scaling factor.
        /// </summary>
        /// <param name="denary"></param>
        /// <param name="scaling"></param>
        public void Normalize(ushort denary, short scaling)
        {
            FromDouble(denary);
            Exponent += scaling;
        }

    }
}
