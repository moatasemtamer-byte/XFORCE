/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 * 
 * Purpose:             Data of ControlBlock.
 * 
 * 
 * DATE        Author   Description
 * --------    ------   ---------------------------------------------------------
 * 2015-04-13   TO     MSWs.

 * 
 ********************************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BL.SafetyData
{
    public class SafetyLogic : XMLTemplate
    {
        public string ProgramName { get; set; }
        public string Name { get; set; }
        public string Template { get; set; }
        public string SheetNumber { get; set; }
        public XElement MyXMLSheet { get; set; }

       
        public Dictionary<string, inOutVariable> inOutVariableMap { get; set; }

        public Dictionary<string, SafetyBlock> LogicBlockMap { get; set; }
        public Dictionary<string, Comment> CommentMap { get; set; }
        public Dictionary<string, contact> LogiccontactMap { get; set; }
        public Dictionary<string, coil> LogiccoilMap { get; set; }

        public SafetyLogic()
        {
            inOutVariableMap = new Dictionary<string, inOutVariable>();
            LogicBlockMap = new Dictionary<string, SafetyBlock>();
            CommentMap = new Dictionary<string, Comment>();
            LogiccontactMap = new Dictionary<string, contact>();
            LogiccoilMap = new Dictionary<string, coil>();
            
        }




        public SafetyLogic(SafetyTemplate copyFrom)
        {
            this.ProgramName = copyFrom.ProgramName;
            this.Name = copyFrom.Name;
            this.SheetNumber = copyFrom.SheetNumber;
            this.Template = copyFrom.Template;
           
            this.MyXMLSheet = new XElement(copyFrom.MyXMLSheet);
            this.inOutVariableMap = new Dictionary<string, inOutVariable>();

            foreach (KeyValuePair<string, inOutVariable> pair in copyFrom.inOutVariableMap)
            {
                inOutVariable newVar = new inOutVariable(pair.Value);
                this.inOutVariableMap.Add(pair.Key, newVar);

            }

         
            this.LogicBlockMap = new Dictionary<string, SafetyBlock>();

            foreach (KeyValuePair<string, SafetyBlock> pair in copyFrom.SafetyBlockMap)
            {
                SafetyBlock newBlock = new SafetyBlock(pair.Value);
                this.LogicBlockMap.Add(pair.Key, newBlock);

            }

            this.CommentMap = new Dictionary<string, Comment>();

            foreach (KeyValuePair<string, Comment> pair in copyFrom.CommentMap)
            {
                Comment newcomment = new Comment(pair.Value);
                this.CommentMap.Add(pair.Key, newcomment);

            }
            this.LogiccontactMap = new Dictionary<string, contact>();
            foreach (KeyValuePair<string, contact> pair in copyFrom.SafetycontactMap)
            {
                contact newcontact = new contact(pair.Value);
                this.LogiccontactMap.Add(pair.Key, newcontact);

            }
        }

        public string GetName()
        {
            return Name;
        }

        public object GetDataObject()
        {
            return this;
        }
    }
}
