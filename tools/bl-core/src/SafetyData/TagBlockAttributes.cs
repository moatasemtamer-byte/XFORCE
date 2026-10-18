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
    public class TagBlockAttributes
    {

        public string AttributeType { get; set; }
        public string TemplateName { get; set; }
        public string Tagname { get; set; }
        public string VariablType { get; set; }
        public string DESCRIPTION { get; set; }
        public string GROUP1 { get; set; }
        public string GROUP2 { get; set; }
        public string ALIASTYPE { get; set; }
        public string DATATYPE { get; set; }
        public string POINTTYPE { get; set; }
        public string CHASSIS { get; set; }
        public string SLOT { get; set; }
        public string POINT { get; set; }
        public string TRUESTATE { get; set; }
        public string FALSESTATE { get; set; }
        public string TRUECOLOR { get; set; }
        public string FALSECOLOR { get; set; }
        public string SignalType { get; set; }
        public string MULTIWRITE { get; set; }
        public string BlockName { get; set; }
        public string BlockType { get; set; }
        public string SheetName { get; set; }
        public string SheetNumber  { get; set; }

        public TagBlockAttributes()
        {
            SignalType = string.Empty;
            DATATYPE = "";
        }

        public TagBlockAttributes(TagBlockAttributes copyFrom)
        {
            this.AttributeType = copyFrom.AttributeType;
            this.TemplateName = copyFrom.TemplateName;
            this.Tagname = copyFrom.Tagname;
            this.VariablType = copyFrom.VariablType;
            this.DESCRIPTION = copyFrom.DESCRIPTION;
            this.GROUP1 = copyFrom.GROUP1;
            this.GROUP2 = copyFrom.GROUP2;
            this.ALIASTYPE = copyFrom.ALIASTYPE;
            this.DATATYPE = copyFrom.DATATYPE;
            this.POINTTYPE = copyFrom.POINTTYPE;
            this.CHASSIS = copyFrom.CHASSIS;
            this.SLOT = copyFrom.SLOT;
            this.POINT = copyFrom.POINT;
            this.TRUESTATE = copyFrom.TRUESTATE;
            this.FALSESTATE = copyFrom.FALSESTATE;
            this.TRUECOLOR = copyFrom.TRUECOLOR;
            this.FALSECOLOR = copyFrom.FALSECOLOR;
            this.SignalType = copyFrom.SignalType;
            this.MULTIWRITE = copyFrom.MULTIWRITE;
            this.BlockName = copyFrom.BlockName;
            this.BlockType = copyFrom.BlockType;
            this.SheetName = copyFrom.SheetName;
            this.SheetNumber = copyFrom.SheetNumber;
          


        }
    }
}
