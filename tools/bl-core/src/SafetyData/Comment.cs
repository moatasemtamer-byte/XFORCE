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
using System.Xml.Linq;

namespace BL.SafetyData
{
    public class Comment
    {
        public string localId { get; set; }
        public XElement MyXML { get; set; }
        public string content { get; set; }
        public string SheetNumber { get; set; }




        public Comment()
        {


        }

        public Comment(Comment copyFrom)
        {
            this.localId = copyFrom.localId;
            this.MyXML = new XElement(copyFrom.MyXML);
            this.content = copyFrom.content;
            this.SheetNumber = copyFrom.SheetNumber;

        }
    }
}
