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
    public class Stage
    {

        public XElement MyXML { get; set; }
        public position Starting { get; set; }
        public int EndYdirection { get; set; }

        public int DifferenceY { get; set; }

        public int Order { get; set; }
        public string Type { get; set; }
        public Stage()
        {
            DifferenceY = 3;
           
            Starting = new position();


        }

        public Stage(Stage copyFrom)
        {
            //this.MyXML = new XElement(copyFrom.MyXML);
            this.Starting = copyFrom.Starting;
            this.EndYdirection = copyFrom.EndYdirection;
            this.DifferenceY = copyFrom.DifferenceY;
            this.Order = copyFrom.Order;
            this.Type = copyFrom.Type;
        }
    }
}
