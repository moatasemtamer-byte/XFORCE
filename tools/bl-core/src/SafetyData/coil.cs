/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 * 
 * Purpose:             Data of ControlBlock.
 * 
 * 
 * DATE        Author   Description
 * --------    ------   ---------------------------------------------------------
 * 2015-04-13   TO     MSW.
 *
 * 
 ********************************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BL.SafetyData
{
    public class coil
    {
        public string typeName { get; set; }
        public string Name { get; set; }
        public string localId { get; set; }
        public XElement MyXML { get; set; }
        public XElement MyInterfaceXML { get; set; }
        public XElement MyGlobalConfigXML { get; set; }
        public string width { get; set; }
        public string height { get; set; }
        public string storage { get; set; }
        public string Logic { get; set; }
        public string timer { get; set; }
        public string Coiltype { get; set; }
        public string twinCoiltype { get; set; }
        public string Networknumber { get; set; }
        public string Parrallelcoilstring { get; set; }
        public List<string> NetworkDescription { get; set; }
        public position positionMap { get; set; }

        //public ParameterList ParameterList { get; set; }
        public contactconnectionPointIn connectionPointInMap { get; set; }
        public connectionPointOut connectionPointOutMap { get; set; }
        public TagBlockAttributes BlockAttributes { get; set; }
        public SafetyLogic MyLogic { get; set; }


        public coil()
        {
            connectionPointInMap = new contactconnectionPointIn();
           
            connectionPointOutMap = new connectionPointOut();
            BlockAttributes = new TagBlockAttributes();
            
            positionMap = new position();
            BlockAttributes.AttributeType = "coil";
            MyLogic = new SafetyLogic();
            Coiltype = "";
        }

        public coil(coil copyFrom)
        {
            this.typeName = copyFrom.typeName;

            this.Name = copyFrom.Name;

            this.localId = copyFrom.localId;
            //this.MyXML = copyFrom.MyXML;
            this.MyXML = new XElement(copyFrom.MyXML);
            this.width = copyFrom.width;
            this.height = copyFrom.height;
            this.MyLogic = copyFrom.MyLogic;
            this.connectionPointInMap = new contactconnectionPointIn(copyFrom.connectionPointInMap);
            
            this.connectionPointOutMap = new connectionPointOut(copyFrom.connectionPointOutMap);
            
            this.positionMap = new position(copyFrom.positionMap);
            this.BlockAttributes = new TagBlockAttributes(copyFrom.BlockAttributes);
        }
    }
}
