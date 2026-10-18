/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 * 
 * Purpose:             as defined in xml
 * 
 * 
 * DATE        Author   Description
 * --------    ------   ---------------------------------------------------------
 * 
 * 
 ********************************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BL.SymbolData
{
    public class Template:XMLTemplate
    {
        public string Name { get; set; }
        public string TemplateName { get; set; }
        public string PIDName { get; set; }
        public string PathName { get; set; }
        public string OldName { get; set; }
    
        public XElement MyXML { get; set; }
        public List<Block> SymbolBlockMap { get; set; }

        public Template()
        {
            
            SymbolBlockMap = new List<Block>();
            PIDName = "----";
            PathName="C:\\";
        }

        public string GetName()
        {
            return Name;
        }

        public object GetDataObject()
        {
            return this;
        }

        public Template(Template copyFrom)
        {
           // Template seriliazecopyFrom = XMLOperations_RuleSet.DeserializeTemplate(new XElement(copyFrom.MyXML));
            this.Name = copyFrom.Name;
            this.TemplateName = copyFrom.TemplateName;
            this.PIDName =copyFrom.PIDName;
            this.PathName = copyFrom.PathName;
            this.MyXML =new XElement(copyFrom.MyXML);
            this.OldName = copyFrom.OldName;
            //this.Type = copyFrom.Type;
            //this.SignalType = copyFrom.SignalType;

            this.SymbolBlockMap = new List<Block>();
            foreach (Block pair in copyFrom.SymbolBlockMap)
            {
                Block newPar = new Block(pair);
                var BlockList = (from b in MyXML.Descendants()
                             where b.Name.LocalName == "Block" && b.Attribute("Name").Value==newPar.Name
                                      select b).FirstOrDefault();
                newPar.MyXML = BlockList;

                var CustomPropertyList = (from b in newPar.MyXML.Descendants()
                                          where b.Name.LocalName == "CustomProperty"
                                          select b).ToList();

                foreach (var CustomProperty in CustomPropertyList)
                {
                    if (CustomProperty != null)
                    {
                        string Name = CustomProperty.Attribute("Name").Value;
                        var CusProp = newPar.CustomPropertyMap.FirstOrDefault(prop => prop.Value.Name == Name).Value;
                        if (CusProp != null)
                        {
                            CusProp.MyXMLProperty = CustomProperty;
                        }


                    }
                }
                this.SymbolBlockMap.Add(newPar);

            }


            //var CustomPropertyList = (from b in MyXML.Descendants()
            //                          where b.Name.LocalName == "CustomProperty"
            //                          select b).ToList();

            //foreach (var CustomProperty in CustomPropertyList)
            //{
            //    if (CustomProperty != null)
            //    {                   
            //       string Name = CustomProperty.Attribute("Name").Value;
            //       var CusProp = SymbolBlockMap.FirstOrDefault().CustomPropertyMap.FirstOrDefault(prop => prop.Value.Name == Name).Value;
            //       if (CusProp != null)
            //       {
            //           CusProp.MyXMLProperty = CustomProperty;
            //       }
                                

            //    }
            //}
        }
    

    }
}
