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
    public class program
    {
        public string Name { get; set; }
        public string version { get; set; }
        public XElement MyXML { get; set; }
        public XElement globalVarsXML { get; set; }
        public List<string> MyLogicList { get; set; }
        public XElement localVarsXML { get; set; }
        public XElement ExecutionListXML { get; set; }
        public SortedDictionary<int, int> IDsMap { get; set; }
        public string pouType { get; set; }

        public Dictionary<string, SafetyTemplate> SafetyTemplateMap { get; set; }
        public Dictionary<string, inOutVariable> VariableMap { get; set; }

        public program()
        {
            SafetyTemplateMap = new Dictionary<string, SafetyTemplate>();
            IDsMap = new SortedDictionary<int, int>();
            VariableMap = new Dictionary<string, inOutVariable>();
            MyLogicList = new List<string>();
        }
    }
}