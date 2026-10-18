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
    public class SafetyBlock
    {
        public string typeName { get; set; }

        public string instanceName { get; set; }
        public string localId { get; set; }
        public XElement MyXML { get; set; }

        public string width { get; set; }
        public string height { get; set; }
        public int Stage { get; set; }
        public position positionMap { get; set; }
        public List<string> Logic { get; set; }

        //public ParameterList ParameterList { get; set; }
        public inputVariables inputVariablesMap { get; set; }

        public outputVariables outputVariablesMap { get; set; }
        public TagBlockAttributes BlockAttributes { get; set; }

        public SafetyBlock()
        {
            inputVariablesMap = new inputVariables();
            //ParameterList = new ParameterList();
            outputVariablesMap = new outputVariables();
            BlockAttributes = new TagBlockAttributes();
            Logic = new List<string>();
            positionMap = new position();
            BlockAttributes.AttributeType = "Block";
        }

        public SafetyBlock(SafetyBlock copyFrom)
        {
            this.typeName = copyFrom.typeName;

            this.instanceName = copyFrom.instanceName;

            this.localId = copyFrom.localId;
            //this.MyXML = copyFrom.MyXML;
            this.MyXML = new XElement(copyFrom.MyXML);
            this.width = copyFrom.width;
            this.height = copyFrom.height;
            this.Stage = copyFrom.Stage;

            this.inputVariablesMap = new inputVariables(copyFrom.inputVariablesMap);// TODO
            foreach (KeyValuePair<string, variable> pair in this.inputVariablesMap.variableMap)
            {

                //pair.Value.connectionPointInMap.connectionMap.MyXML=this.MyXML;

                var inpList = (from e in this.MyXML.Descendants()
                               where e.Name.LocalName == "inputVariables"
                               select e).ToList();

                if (inpList.Count > 0)
                {
                    XElement thisinpvar = inpList[0];


                    var conn = (from s in thisinpvar.Descendants()
                                where s.Name.LocalName == "variable"
                                select s).FirstOrDefault(vr => vr.Attribute("formalParameter").Value == pair.Value.formalParameter);

                    if (conn != null)
                    {
                        var varList = (from k in conn.Descendants()
                                       where k.Name.LocalName == "connection"
                                       select k).ToList();
                        if (varList.Count> 0)
                            pair.Value.connectionPointInMap.connectionMap.MyXML = varList[0];
                    }


                }

            }

            this.outputVariablesMap = new outputVariables(copyFrom.outputVariablesMap);
            serializeoutputVariables(this.outputVariablesMap.variableMap);

            this.positionMap = new position(copyFrom.positionMap);
            this.BlockAttributes = new TagBlockAttributes(copyFrom.BlockAttributes);
        }

        private void serializeoutputVariables(Dictionary<string, variable> variableMap)
        {
            foreach (var myvariable in variableMap)
            {
                var inpList = (from e in this.MyXML.Descendants()
                               where e.Name.LocalName == "outputVariables"
                               select e).ToList();

                if (inpList.Count > 0)
                {
                    XElement thisinpvar = inpList[0];


                    var conn = (from s in thisinpvar.Descendants()
                                where s.Name.LocalName == "variable"
                                select s).FirstOrDefault(vr => vr.Attribute("formalParameter").Value == myvariable.Value.formalParameter);

                    if (conn != null)
                    {
                        myvariable.Value.MyXML = conn;
                    }

                }
            }
        }
    }
}
