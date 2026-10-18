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

using System.Collections.Generic;
using System.Xml.Linq;

namespace BL.SafetyData
{
    public class connection
    {
        public string refLocalId { get; set; }
        public string formalParameter { get; set; }
        public string refContactName { get; set; }
        public string prevLogic { get; set; }
        public string afterLogic { get; set; }
        public System.Xml.Linq.XElement MyXML { get; set; }

        //public ParameterList ParameterList { get; set; }
        //public Dictionary<string, position> positionMap { get; set; }
        public List<position> positionMap { get; set; }

        //public connection()
        //{
        //    //positionMap = new Dictionary<string, position>();

        //    positionMap = new List<position>();
        //    //ParameterList = new ParameterList();
        //}
        public connection()
        {
            //  XNamespace ts = "http://www.triconex.com/controller/03/2008";

            XNamespace ts = "http://www.plcopen.org/xml/tc6.xsd";
            positionMap = new List<position>();

            MyXML = new XElement(ts+"connection");
            MyXML.Add(new XAttribute("refLocalId", ""));
        }

        public connection(string inprefLocalId, string inpformalParameter)
        {
            //XNamespace ts = "http://www.triconex.com/controller/03/2008";
            XNamespace ts = "http://www.plcopen.org/xml/tc6.xsd";
            positionMap = new List<position>();

            MyXML = new XElement(ts+"connection");
            MyXML.Add(new XAttribute("refLocalId", inprefLocalId));
            if (inpformalParameter != "")
            {
                MyXML.Add(new XAttribute("formalParameter", inpformalParameter));
            }
            refLocalId = inprefLocalId;
            formalParameter = inpformalParameter;
        }

        public connection(connection copyFrom)
        {
            this.refLocalId = copyFrom.refLocalId;
            this.formalParameter = copyFrom.formalParameter;
            this.MyXML = copyFrom.MyXML == null ? null : new XElement(copyFrom.MyXML);
            //this.positionMap = new Dictionary<string, position>(copyFrom.positionMap);
            this.positionMap = new List<position>(copyFrom.positionMap);

            // TODO
            //this.ParameterList = copyFrom.ParameterList == null ? null : new ParameterList(copyFrom.ParameterList);
        }
    }
}