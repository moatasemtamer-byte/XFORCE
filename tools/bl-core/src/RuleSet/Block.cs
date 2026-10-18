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



namespace BL.SymbolData
{
    public class Block
    {
        public string Name { get; set; }
        public string PIDName { get; set; }
       
        public Dictionary<string, Property> CustomPropertyMap { get; set; }
        public XElement MyXML { get; set; }

        public Block()
        {
            CustomPropertyMap = new Dictionary<string, Property>();
            PIDName = "NewDisplay";
        }

        public Block(Block copyFrom)
        {
            this.Name = copyFrom.Name;
            this.PIDName = copyFrom.PIDName;
            this.MyXML = new XElement(copyFrom.MyXML);
      

            this.CustomPropertyMap = new Dictionary<string, Property>();
            foreach (KeyValuePair<string, Property> pair in copyFrom.CustomPropertyMap)
            {
                Property newPar = new Property(pair.Value);
                this.CustomPropertyMap.Add(pair.Key, newPar);

            }

            var CustomPropertyList = (from b in this.MyXML.Descendants()
                                      where b.Name.LocalName == "CustomProperty"
                                      select b).ToList();

            foreach (var CustomProperty in CustomPropertyList)
            {
                if (CustomProperty != null)
                {
                    string Name = CustomProperty.Attribute("Name").Value;
                    var CusProp = this.CustomPropertyMap.FirstOrDefault(prop => prop.Value.Name == Name).Value;
                    if (CusProp != null)
                    {
                        CusProp.MyXMLProperty = CustomProperty;
                    }


                }
            }
        }

      
    }
}
