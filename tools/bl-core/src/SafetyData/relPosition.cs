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
    public class relPosition
    {

        public XElement MyXML { get; set; }
        public string X { get; set; }
        public string Y { get; set; }


        public relPosition()
        {
            // XNamespace ts = "http://www.triconex.com/controller/03/2008";
            XNamespace ts = "http://www.plcopen.org/xml/tc6.xsd";
            X = "0";
            Y = "0";
            MyXML = new XElement(ts+"relPosition");
            MyXML.Add(new XAttribute("x", "0"));
            MyXML.Add(new XAttribute("y", "0"));

        }

        public relPosition(string inX, string inY)
        {
            // XNamespace ts = "http://www.triconex.com/controller/03/2008";
            XNamespace ts = "http://www.plcopen.org/xml/tc6.xsd";

            X = inX;
            Y = inY;
            MyXML = new XElement(ts+"relPosition");
            MyXML.Add(new XAttribute("x", inX));
            MyXML.Add(new XAttribute("y", inY));
        }

        public relPosition(relPosition copyFrom)
        {
            this.MyXML= copyFrom.MyXML == null ? null : new XElement(copyFrom.MyXML);
            this.X = copyFrom.X;
            this.Y = copyFrom.Y;

        }
    }
}
