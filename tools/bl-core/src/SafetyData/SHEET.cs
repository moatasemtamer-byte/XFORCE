/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 * 
 * Purpose:             Data of ControlBlock.
 * 
 * 
 * DATE        Author   Description
 * --------    ------   ---------------------------------------------------------
 * 
 ********************************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BL.SafetyData
{
    public class SHEET
    {


        public string Number { get; set; }

        public string SHEET_TITLE { get; set; }

        public SHEET()
        {

        }

        public SHEET(SHEET copyFrom)
        {
            this.Number = copyFrom.Number;

            this.SHEET_TITLE = copyFrom.SHEET_TITLE;

        }
    }
}
