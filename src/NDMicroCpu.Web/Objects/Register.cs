//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using MudBlazor;

namespace NDMicroCpu.Web.Objects
{

    /// <summary>
    /// Helper class to keep data update the UI.
    /// Used by Registers.razor and more
    /// </summary>
    public class Register
    {
        /// <summary>
        /// Raised when the register value is read for display.
        /// </summary>
        public event EventHandler<UpdateRegisterEvent>? OnReadRegister;
        /// <summary>
        /// Raised when the user has written a new value in the register field.
        /// </summary>
        public event EventHandler<UpdateRegisterEvent>? OnWriteRegister;

        #region Internal variables
        const string _updated_adornment = "!";
        string _description = "";
        string _label = "";

        // To lock UI to readonly
        /// <summary>
        /// True locks the register field in the user interface to read-only.
        /// </summary>
        public bool ReadOnly { get; set; }

        private bool isUpdated { get; set; }
        int _r;
        ushort _id;
        #endregion Internal variables

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="id">
        /// The register number.
        /// </param>
        /// <param name="label">
        /// The short name shown as the field label.
        /// </param>
        public Register(ushort id, string label = "")
        {
            _id = id;
            _r = 0;
            _label = label;
        }


        /// <summary>
        /// The short name shown as the field label.
        /// </summary>
        public string Label
        {
            get => _label;
        }

        /// <summary>
        /// The value as octal digits, the form the user interface shows and edits.
        /// </summary>
        public string OctalValue
        {
            get => CheckForNewReturnR();
            set => InternalSet(value);
        }


        /// <summary>
        /// The register number.
        /// </summary>
        public int Value { get; }
        /// <summary>
        /// The description shown as the field tooltip.
        /// </summary>
        public string Description { get => _description; }

        /// <summary>
        /// The MudBlazor variant the field is drawn with; changes when the value was updated.
        /// </summary>
        public Variant variant
        {
            get => isUpdated ? Variant.Filled : Variant.Outlined;
        }


        /// <summary>
        /// The adornment text shown while the value counts as updated, empty otherwise.
        /// </summary>
        public string adornment
        {
            get => isUpdated ? _updated_adornment : "";
        }

        /// <summary>
        /// Sets a new value and, by default, marks the register as updated so the user interface highlights it.
        /// </summary>
        /// <param name="val">
        /// The new value.
        /// </param>
        /// <param name="update">
        /// False sets the value without the highlight.
        /// </param>
        public void UpdateValue(int val, bool update = true)
        {
            if (val != _r)
            {
                _r = val;
                isUpdated = update;
            }
            else
            {
                isUpdated = false;
            }
        }
        /// <summary>
        /// Clears the updated highlight.
        /// </summary>
        public void ClearUpdated()
        {
            isUpdated = false;
        }

        string CheckForNewReturnR()
        {
            CheckForNewValue();
            return Int2Oct(_r);
        }

        void CheckForNewValue()
        {
            if (OnReadRegister != null)
            {
                UpdateRegisterEvent args = new UpdateRegisterEvent(_id, _r);
                OnReadRegister?.Invoke(this, args);
                if (args.handled)
                {
                    if (_r != args.Value)
                    {
                        _r = args.Value;
                        isUpdated = true;
                    }
                }
            }
        }
        void WriteUpdatedValue(int value)
        {
            if (OnWriteRegister != null)
            {
                UpdateRegisterEvent args = new UpdateRegisterEvent(_id, value);
                OnWriteRegister?.Invoke(this, args);
            }
        }

        /// <summary>
        /// Sets the tooltip text.
        /// </summary>
        /// <param name="txt">
        /// The description.
        /// </param>
        public void SetDescription(string txt)
        {
            _description = txt;
        }

        /// <summary>
        /// Sets the field label.
        /// </summary>
        /// <param name="txt">
        /// The label.
        /// </param>
        public void SetLabel(string txt)
        {
            _label = txt;
        }


        /// <summary>
        /// Set value and clear updated flag
        /// </summary>
        /// <param name="val"></param>
        private void InternalSet(string val)
        {
            int newr = Oct2Int(val);
            if (_r != newr)
            {
                _r = newr;
                isUpdated = false;

                WriteUpdatedValue(newr);
            }
        }


        private string Int2Oct(int val)
        {
            return Convert.ToString(val, 8).PadLeft(6, '0');
        }

        private int Oct2Int(string oct)
        {
            try
            {
                return Convert.ToInt32(oct, 8);
            }
            catch (Exception)
            {

                return 0;
            }

        }
    }
}
