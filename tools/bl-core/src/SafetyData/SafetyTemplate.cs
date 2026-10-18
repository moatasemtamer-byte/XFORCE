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
    public class SafetyTemplate:XMLTemplate
    {
        public string ProgramName { get; set; }

        public string CreateNewProgram { get; set; }
        public string Name { get; set; }
        public string Template { get; set; }
        public string SheetNumber { get; set; }
        public int Networkcount { get; set; }
        public XElement MyXMLSheet { get; set; }

        //public DeclarationList DeclarationList { get; set; }
        //public ControlBlockList ControlBlockList { get; set; }
        public Dictionary<string, inOutVariable> inOutVariableMap { get; set; }

        public Dictionary<string, SafetyBlock> SafetyBlockMap { get; set; }
        public Dictionary<string, Comment> CommentMap { get; set; }
        public Dictionary<string, contact> SafetycontactMap { get; set; }
        public Dictionary<string, coil> SafetycoilMap { get; set; }
        public Dictionary<string, Stage> StageMap { get; set; }
        public List<string> NetworkDescription { get; set; }

        public SafetyTemplate()
        {
            inOutVariableMap = new Dictionary<string, inOutVariable>();
            SafetyBlockMap = new Dictionary<string, SafetyBlock>();
            CommentMap = new Dictionary<string, Comment>();
            SafetycontactMap = new Dictionary<string, contact>();
            SafetycoilMap = new Dictionary<string, coil>();
            StageMap = new Dictionary<string, Stage>();
            //DeclarationList = new DeclarationList();
            //ControlBlockList = new ControlBlockList();
            NetworkDescription = new List<string>();
            Networkcount = 0;
        }

        /*public StrategyComponent(StrategyComponent from)
        {
            CopyFrom(from);
        }

        public void CopyFrom(StrategyComponent from)
        {
            Name = from.Name;
            Template = from.Template;
            DeclarationList = from.DeclarationList == null ? null : new DeclarationList(from.DeclarationList);
            ControlBlockList = from.ControlBlockList == null ? null : new ControlBlockList(from.ControlBlockList);
        }*/

        public SafetyTemplate(SafetyTemplate copyFrom)
        {
            this.ProgramName = copyFrom.ProgramName;
            this.Name = copyFrom.Name;
            this.SheetNumber = copyFrom.SheetNumber;
            this.Template = copyFrom.Template;
            CreateNewProgram = copyFrom.CreateNewProgram;
            //this.MyXMLSheet = copyFrom.MyXMLSheet;
           // this.inOutVariableMap = copyFrom.inOutVariableMap;
            //this.SafetyBlockMap = copyFrom.SafetyBlockMap;
            this.MyXMLSheet = new XElement(copyFrom.MyXMLSheet);
            this.inOutVariableMap = new Dictionary<string, inOutVariable>();

            foreach (KeyValuePair<string, inOutVariable> pair in copyFrom.inOutVariableMap)
            {
                inOutVariable newVar = new inOutVariable(pair.Value);
                this.inOutVariableMap.Add(pair.Key, newVar);

            }

            //

            //this.SafetyBlockMap = copyFrom.SafetyBlockMap;
            //
            this.SafetyBlockMap = new Dictionary<string, SafetyBlock>();


            foreach (KeyValuePair<string, SafetyBlock> pair in copyFrom.SafetyBlockMap)
            {
                SafetyBlock newBlock = new SafetyBlock(pair.Value);
                this.SafetyBlockMap.Add(pair.Key, newBlock);

            }

            this.CommentMap = new Dictionary<string, Comment>();

            foreach (KeyValuePair<string, Comment> pair in copyFrom.CommentMap)
            {
                Comment newcomment = new Comment(pair.Value);
                this.CommentMap.Add(pair.Key, newcomment);

            }
            this.SafetycontactMap = new Dictionary<string, contact>();
            foreach (KeyValuePair<string, contact> pair in copyFrom.SafetycontactMap)
            {
                contact newcontact = new contact(pair.Value);
                this.SafetycontactMap.Add(pair.Key, newcontact);

            }
            this.SafetycoilMap = new Dictionary<string, coil>();
            foreach (KeyValuePair<string, coil> pair in copyFrom.SafetycoilMap)
            {
                coil newcoil = new coil(pair.Value);
                this.SafetycoilMap.Add(pair.Key, newcoil);

            }
            this.StageMap = new Dictionary<string, Stage>();
            foreach(KeyValuePair<string, Stage> pair in copyFrom.StageMap)
            {
                Stage newstage = new Stage(pair.Value);
                this.StageMap.Add(pair.Key, newstage);

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
