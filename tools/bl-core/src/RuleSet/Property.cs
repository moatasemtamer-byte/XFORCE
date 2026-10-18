/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 * 
 * Purpose:             Data of ControlBlock's Parameter.
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
    public class Property
    {
        //public const int VISIBILITY_Public    = 0;
        public const int VISIBILITY_Public = 1;
        public const int VISIBILITY_RIGHT   = 2;

        public string Name { get; set; }
        public string Rule { get; set; }
        public string BlockName { get; set; }
        public string Description { get; set; }
        public string Expression { get; set; }
        public string DataType { get; set; }
        public string ValueType { get; set; }
        public string Configurable { get; set; }
        public string Settable { get; set; }
        public string ConnectionType { get; set; }
        public int Visible { get; set; }
        public string DataStore { get; set; }
        public XElement MyXMLProperty { get; set; }
       

        public  Property()
        {
            Visible = VISIBILITY_Public;
        }

        public bool Modify(string modprop)
        {
            bool retvalue=false;
            Expression = modprop;
            var explist = (from HH in MyXMLProperty.Descendants()
             where HH.Name.LocalName == "Expression"
             select HH).FirstOrDefault();

            if (explist != null)
                explist.Attribute("Expression").Value = modprop;
            return retvalue;
        
        }

        public Property(Property copyFrom)
        {
            this.Name = copyFrom.Name;
            this.BlockName = copyFrom.BlockName;
            this.Expression = copyFrom.Expression;
            this.Configurable = copyFrom.Configurable;
            this.Settable = copyFrom.Settable;
            this.DataType = copyFrom.DataType;
            this.DataStore = copyFrom.DataStore;
            this.Visible = copyFrom.Visible;
            this.ValueType = copyFrom.ValueType;
            this.ConnectionType = copyFrom.ConnectionType;
            this.MyXMLProperty = new XElement(copyFrom.MyXMLProperty);
        }
    }
}
