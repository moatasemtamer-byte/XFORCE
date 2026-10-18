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
    public class expression
    {


        public string Tagname { get; set; }



        public expression()
        {

        }

        public expression(expression copyFrom)
        {
            this.Tagname = copyFrom.Tagname;


        }
    }
}
