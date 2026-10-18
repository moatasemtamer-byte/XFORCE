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
    public class inOutVariable
    {
        public string localId { get; set; }
        public XElement MyXML { get; set; }
        public XElement MyInterfaceXML { get; set; }
        public XElement MyGlobalConfigXML { get; set; }
        public string width { get; set; }
        public string height { get; set; }
        public string VariableType { get; set; }

        public position positionMap { get; set; }
        //public ParameterList ParameterList { get; set; }
        public connection connectionMap { get; set; }
        public expression expressionMap { get; set; }
        public TagBlockAttributes TagAttributesMap { get; set; }



        public inOutVariable()
        {
            expressionMap = new expression();
            connectionMap = new connection();
            positionMap = new position();
            TagAttributesMap = new TagBlockAttributes();
            TagAttributesMap.AttributeType = "TAG";

        }

        public inOutVariable(inOutVariable copyFrom)
        {
            this.localId = copyFrom.localId;
           // this.MyXML = copyFrom.MyXML;
            //this.MyInterfaceXML = copyFrom.MyInterfaceXML;
            this.MyXML = copyFrom.MyXML == null ? null : new XElement(copyFrom.MyXML);
            this.MyInterfaceXML = copyFrom.MyInterfaceXML == null ? null : new XElement(copyFrom.MyInterfaceXML);
            this.MyGlobalConfigXML = copyFrom.MyGlobalConfigXML == null ? null : new XElement(copyFrom.MyGlobalConfigXML);
            this.width = copyFrom.width;
            this.height = copyFrom.height;
            this.VariableType = copyFrom.VariableType;
            this.connectionMap = new connection(copyFrom.connectionMap);
            getConnectionXML();
            this.expressionMap = new expression(copyFrom.expressionMap);
            this.positionMap = new position(copyFrom.positionMap);
            this.TagAttributesMap = new TagBlockAttributes(copyFrom.TagAttributesMap);
        }

        private void getConnectionXML()
        {
            var connectionlist = (from L in this.MyXML.Descendants()
                                  where L.Name.LocalName == "connection"
                                  select L).ToList();

            if (connectionlist.Count() > 0)
            {
                this.connectionMap.MyXML = connectionlist[0];

            }

        }
    }
}
