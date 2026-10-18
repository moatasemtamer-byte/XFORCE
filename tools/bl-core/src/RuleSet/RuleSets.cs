/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 * 
 * Purpose:             as defined in xml
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
    public class RuleSets:XMLTemplate
    {
        public string Name { get; set; }
       
      
        public string PathName { get; set; }
   
    
        public XElement MyXML { get; set; }
        public List<Template> TemplateMap { get; set; }

        public RuleSets()
        {

            TemplateMap = new List<Template>();
            
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

        public RuleSets(RuleSets copyFrom)
        {
            this.Name = copyFrom.Name;
         
            this.PathName = copyFrom.PathName;
            this.MyXML = new XElement(copyFrom.MyXML);
        
            

            this.TemplateMap = new List<Template>();
            foreach (Template pair in copyFrom.TemplateMap)
            {
                Template newPar = new Template(pair);
                this.TemplateMap.Add(newPar);
            }


         
        }

       
    

    }
}
