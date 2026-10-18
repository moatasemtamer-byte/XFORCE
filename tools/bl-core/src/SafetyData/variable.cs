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
    public class variable
    {


        public const int VISIBILITY_NONE = 0;
        public const int VISIBILITY_LEFT = 1;
        public const int VISIBILITY_RIGHT = 2;
        public string formalParameter { get; set; }
        public string fatherblockID { get; set; }
        public int Visible { get; set; }
        public XElement MyXML { get; set; }

        //public ParameterList ParameterList { get; set; }
        public connectionPointIn connectionPointInMap { get; set; }
        public connectionPointOut connectionPointOutMap { get; set; }
        public variable()
        {
            connectionPointInMap = new connectionPointIn();
            //ParameterList = new ParameterList();connectionPointOut
            connectionPointOutMap = new connectionPointOut();
            Visible = VISIBILITY_NONE;
        }

        public variable(variable copyFrom)
        {
            this.formalParameter = copyFrom.formalParameter;
            this.fatherblockID = copyFrom.fatherblockID;
            this.MyXML = copyFrom.MyXML == null ? null : new XElement(copyFrom.MyXML);
            this.connectionPointInMap = new connectionPointIn(copyFrom.connectionPointInMap);// TODO
            //forwardtoConnection();
            //this.ParameterList = copyFrom.ParameterList == null ? null : new ParameterList(copyFrom.ParameterList);
            this.connectionPointOutMap = new connectionPointOut(copyFrom.connectionPointOutMap);
            this.Visible = copyFrom.Visible;

        }

     
    }
}
