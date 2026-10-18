/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 *
 * Purpose:             
 *
 *
 * DATE        Author   Description
 * --------    ------   ---------------------------------------------------------
 *
 ********************************************************************************************/

using BL.SafetyData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BL
{
    public class XMLOperations_LadderLogic
    {
        //public static string CoilContactWidth = "18";
        //public static string CoilContactHeight = "8";
        public static string CoilContactWidth;
        public static string CoilContactHeight;
        public static string TemplateSelction ;

        public static string CommentTextSize ;//for templat b
        private static string _PathData = XMLOperations_RuleSet.GetDataDirectory();
      

        public static Dictionary<string, inOutVariable> globalVariableMap;

        public static string BlankfullPath;
        public static string BlankPT2fullPath;

        public static SafetyBlock BlankBlock;
        public static SafetyBlock MovekBlock;
        public static XElement blankPT2;

        public static List<program> BlankProgram;
       // string tt = BlankProgram.FirstOrDefault().Name;

        public static void UpdateSetting()
        {
            // CommentTextSize = TemplateSelction == "B" ? "12" : "18";//for templat b//nesma
            CommentTextSize = TemplateSelction == "B" ? "12" : "14";//for templat b

            //CoilContactWidth = TemplateSelction == "B" ? "16":"18"; //NESMA
            CoilContactWidth = TemplateSelction == "B" ? "16" : "20";
            //CoilContactHeight = TemplateSelction == "B" ? "10" : "8"; //NESMA
            //CoilContactHeight = TemplateSelction == "B" ? "10" : "20";
            CoilContactHeight = TemplateSelction == "B" ? "10" : "11";
        }


        public static void Intialize()
        {
           BlankfullPath = System.IO.Path.Combine(_PathData, "BlankData", "Block.xml");
            if(TemplateSelction == "B")
            {
                BlankPT2fullPath = System.IO.Path.Combine(_PathData, "BlankData", "PT2B.xml");
            }

            if (TemplateSelction == "D")
            {
                BlankPT2fullPath = System.IO.Path.Combine(_PathData, "BlankData", "PT2D.xml");
            }

            BlankBlock = ReadBlankBlock();
          MovekBlock = RearMovekBlock();
          blankPT2 = FileWorks.LoadXMLFile(BlankPT2fullPath);

         BlankProgram = ReadBlankPT2(blankPT2);
           string tt = BlankProgram.FirstOrDefault().Name;
        }

        public static List<program> DeserializeSafetyProgram(XElement loadedXml)
        {
            var ioFlgList = (from b in loadedXml.Descendants()
                             where b.Name.LocalName == "pou" && b.Attribute("pouType").Value == "program"
                             select b).ToList();

            var configurations = (from b in loadedXml.Descendants()
                                  where b.Name.LocalName == "configurations"
                                  select b).FirstOrDefault();

            List<program> newPrograms = new List<program>();
            XMLOperations_LadderLogic.globalVariableMap = new Dictionary<string, inOutVariable>();
            Dictionary<string, inOutVariable> globalVariableMap = new Dictionary<string, inOutVariable>();
            //  int i = 1;

            foreach (var PrgInioFlgList in ioFlgList)
            {
                if (PrgInioFlgList != null)
                {
                    program newProgram = new program();

                    var LOCvariableList = (from HH in PrgInioFlgList.Descendants()
                                           where HH.Name.LocalName == "localVars" && !(HH.ToString().Contains("Constants"))
                                           select HH).FirstOrDefault();
                    var globalvariableList = (from HH in PrgInioFlgList.Descendants()
                                              where HH.Name.LocalName == "globalVars" && !(HH.ToString().Contains("Constants"))
                                              select HH).FirstOrDefault();
                    //read Safetytemplatename
                    newProgram.Name = PrgInioFlgList.Attribute("name").Value;
                    newProgram.version = PrgInioFlgList.Attribute("version").Value;
                    newProgram.pouType = "program";
                    newProgram.MyXML = PrgInioFlgList;

                    newProgram.globalVarsXML = globalvariableList;
                    newProgram.localVarsXML = LOCvariableList;
                    var ExecutionList = (from HH in configurations.Descendants()
                                         where HH.Name.LocalName == "pouInstance" && HH.Attribute("name").Value == newProgram.Name
                                         select HH).FirstOrDefault();
                    if (ExecutionList != null)
                    {
                        newProgram.ExecutionListXML = ExecutionList;
                    }
                    List<string> mismatches = new List<string>();
                    Dictionary<string, SafetyTemplate> newSafetyTemplateMap = ProgramOperations_Safety.DeserializeSafetyTemplate(PrgInioFlgList, newProgram.Name);
                    SortedDictionary<int, int> IDS = new SortedDictionary<int, int>();

                    ReadIOvariable(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
                    ReadContact(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
                    ReadCoil(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
                    ReadBlocks(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
                    ReadCoillogic(ref newSafetyTemplateMap);
                    XElement myproxml = PrgInioFlgList;
                    BuildCoillogic(ref newSafetyTemplateMap, ref IDS, ref myproxml, mismatches);

                    // ReadTagsConf(ref newSafetyTemplateMap, loadedXml);
                    ReadTagsConf(ref newSafetyTemplateMap, PrgInioFlgList, configurations);

                    ReadComment(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);

                    newProgram.IDsMap = IDS;
                    newProgram.SafetyTemplateMap = newSafetyTemplateMap;
                    ReadLoacalVariable(ref newProgram);
                    newPrograms.Add(newProgram);
                }
            }
            return newPrograms;
        }

        public static void BuildCoillogic(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref SortedDictionary<int, int> IDs, ref XElement myprogamxml, List<string> mismatches)
        {
            foreach (SafetyTemplate st in newSafetyTemplateMap.Values)
            {
                SafetyTemplate currentst = st;

                ProgramOperations_Safety.BuildLogic(ref currentst, ref IDs,mismatches);
                AddCoilBlocksToProgram(ref currentst, ref myprogamxml);
                AddconstanttoProgram(ref currentst, ref myprogamxml);
                AddcommentstoProgram(ref currentst, ref myprogamxml);
            }
            foreach (SafetyTemplate st in newSafetyTemplateMap.Values.Where(X => X.Name != null && X.Name.ToUpper().Contains("PACK")))
            {
                SafetyTemplate currentst = st;

                ProgramOperations_Safety.BuildPACKLogic(ref currentst, ref IDs);
                ;
            }
        }

        public static void AddcommentstoProgram(ref SafetyTemplate safetyTemplate, ref XElement myprogamxml)
        {
            {
                var FBDList = (from d in myprogamxml.Descendants()
                               where d.Name.LocalName == "FBD"
                               select d).FirstOrDefault();

                foreach (coil mycoil in safetyTemplate.SafetycoilMap.Values)
                {
                    foreach (Comment myblock in mycoil.MyLogic.CommentMap.Values)
                    {
                        
                        
                            FBDList.Add(myblock.MyXML);
                        
                    }
                }
            }
        }

        public static void AddconstanttoProgram(ref SafetyTemplate safetyTemplate, ref XElement myprogamxml)
        {
            var FBDList = (from d in myprogamxml.Descendants()
                           where d.Name.LocalName == "FBD"
                           select d).FirstOrDefault();

            var globalvariableList = (from HH in myprogamxml.Descendants()
                                      where HH.Name.LocalName == "localVars" && (HH.ToString().Contains("Constants"))
                                      select HH).FirstOrDefault();

            foreach (coil mycoil in safetyTemplate.SafetycoilMap.Values)
            {
                foreach (inOutVariable myblock in mycoil.MyLogic.inOutVariableMap.Values)
                {
                    if (myblock.VariableType == "Constant")
                    {
                        FBDList.Add(myblock.MyXML);
                        globalvariableList.Add(myblock.MyInterfaceXML);
                    }
                }
            }
        }

        public static SafetyBlock ReadBlankBlock()
        {
            XElement blankblock = FileWorks.LoadXMLFile(BlankfullPath);
            var thisBlk = (from d in blankblock.Descendants()
                           where d.Name.LocalName == "block"
                           select d).FirstOrDefault();

            SafetyBlock newBlk = ProgramOperations_Safety.DeserializeSafetyBlock(thisBlk);
            return newBlk;
        }

        public static SafetyBlock RearMovekBlock()
        {
            XElement blankblock = FileWorks.LoadXMLFile(BlankfullPath);
            var thisBlk = (from d in blankblock.Descendants()
                           where d.Name.LocalName == "block" && d.Attribute("typeName").Value == "MOVE"
                           select d).FirstOrDefault();

            SafetyBlock newBlk = ProgramOperations_Safety.DeserializeSafetyBlock(thisBlk);
            return newBlk;
        }

        public static SafetyBlock ReadTypeBlock(string blocktype)
        {
            // newBlk=null;

            XElement blankblock = FileWorks.LoadXMLFile(BlankfullPath);
            var thisBlk = (from d in blankblock.Descendants()
                           where d.Name.LocalName == "block" && d.Attribute("typeName").Value == blocktype
                           select d).FirstOrDefault();

            SafetyBlock newBlk = ProgramOperations_Safety.DeserializeSafetyBlock(thisBlk);
            return newBlk;
        }

   

        public static List<program> ReadBlankPT2(XElement pt2)
        {
            //  XElement blankPT2 = FileOperations.LoadXMLFile(BlankPT2fullPath);
            return XMLOperations_safety.DeserializeSafetyProgram(pt2);
        }

        public static SafetyBlock newBlockType(string newType, string sheetnumber)
        {

            SafetyBlock newblock = null;

           

                if (newType != "MOVE" && newType != "NOT")
                {
                    newblock = new SafetyBlock(BlankBlock);
                    ChangeBlockType(ref newblock, newType);
                }
                else
                {
                    newblock = new SafetyBlock(MovekBlock);
                }

                if (newType == "FB04_DI" || newType.Contains("TRIG") || newType.Contains("TP") ||
                     newType.Contains("GT") || newType.Contains("GE") || newType.Contains("TO") || newType == "LT" || newType == "LE" || newType == "TDD_I" || newType == "TDE_I"
                     || newType == "SCALE" || newType == "AIN" || newType == "MEDSEL" || newType == "PERDEV" || newType == "EQ" || newType == "NE" || newType == "TCJ_CONV" || newType == "SR" || newType == "RS" || newType == "TMR_I" || newType == "CTU")
                {
                try
                {
                    newblock = new SafetyBlock(ReadTypeBlock(newType));
                }
                catch(Exception ex)
                {

                }
                    
                }

                if (newType.Contains("STFB"))
                {
                    newblock = new SafetyBlock(ReadTypeBlock("STFB"));
                    Changeblocktypeonly(ref newblock, newType);
                }
                XMLOperations_safety.ModifyBlockPosition(ref newblock, sheetnumber);
          
  
            return newblock;
        }

        internal static inOutVariable newconstant(string newType, string sheetNumber)
        {
            inOutVariable newconstant = null;

            try
            {
                newconstant = new inOutVariable(ReadConstant(newType));


                XMLOperations_safety.ModifyIOvariablePosition(ref newconstant, sheetNumber);
            }
            catch (Exception)
            {

             
            }
          
            return newconstant;
        }

        private static inOutVariable ReadConstant(string newType)
        {
            XElement blankblock = FileWorks.LoadXMLFile(BlankfullPath);
            var thiscons = (from d in blankblock.Descendants()
                            where d.Name.LocalName == "inVariable" && d.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "DATATYPE").Value == newType
                            select d).FirstOrDefault();
            if(thiscons==null)
            {

            }
            inOutVariable newinOutVariable = ProgramOperations_Safety.DeserializeSafetyinOutVariable(thiscons);
            var LOCvariableList = (from HH in blankblock.Descendants()
                                   where HH.Name.LocalName == "localVars" && (HH.ToString().Contains("Constants"))
                                   select HH).FirstOrDefault();

            var variableList = (from VV in LOCvariableList.Descendants()
                                where VV.Name.LocalName == "variable" && VV.Attribute("name").Value == newinOutVariable.expressionMap.Tagname
                                select VV).FirstOrDefault();

            if (variableList != null)
            {
                newinOutVariable.MyInterfaceXML = variableList;
            }
            return newinOutVariable;
        }

        public static void ChangeBlockType(ref SafetyBlock newblock, string newType)
        {
            Changeblocktypeonly(ref newblock, newType);
            newblock.outputVariablesMap.variableMap.FirstOrDefault().Value.formalParameter = newType;

            newblock.outputVariablesMap.variableMap.FirstOrDefault().Value.MyXML.Attribute("formalParameter").Value = newType;
        }

      

        private static void Changeblocktypeonly(ref SafetyBlock newblock, string newType)
        {
            newblock.typeName = newType;
            newblock.MyXML.Attribute("typeName").Value = newType;
        }

        private static void ReadCoillogic(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap)
        {
            foreach (SafetyTemplate st in newSafetyTemplateMap.Values)
            {
                SafetyTemplate currentst = st;

                ProgramOperations_Safety.GetLogic(ref currentst);
            }
        }

        public static void ChangeLadderElementsToFBD(ref SafetyTemplate SafetyTemplate, ref XElement myprogamxml)
        {
            ChangeXMLheader(ref myprogamxml);
            ChangeContactsToVariable(ref SafetyTemplate);
            ChangeCoilsToVariable(ref SafetyTemplate);
        }

        public static void AddCoilBlocksToProgram(ref SafetyTemplate safetyTemplate, ref XElement myprogamxml)
        {
            var FBDList = (from d in myprogamxml.Descendants()
                           where d.Name.LocalName == "FBD"
                           select d).FirstOrDefault();

            foreach (coil mycoil in safetyTemplate.SafetycoilMap.Values)
            {
                foreach (SafetyBlock myblock in mycoil.MyLogic.LogicBlockMap.Values)
                {
                    if (myblock.typeName == "OR" || myblock.typeName == "AND" || myblock.typeName == "AIN" || myblock.typeName == "TCJ_CONV"|| myblock.typeName == "SR" || myblock.typeName == "RS" || myblock.typeName == "TMR_I"|| myblock.typeName == "CTU" || myblock.typeName == "SCALE"|| myblock.typeName == "MEDSEL" || myblock.typeName == "PERDEV"
                        || myblock.typeName == "MOVE" || myblock.typeName == "NOT"|| myblock.typeName == "TP"|| myblock.typeName == "TP_I"
                        || myblock.typeName == "FB04_DI" || myblock.typeName.Contains("TRIG") || myblock.typeName.Contains("GE") || myblock.typeName.Contains("GT") || myblock.typeName.Contains("TDD_I")|| myblock.typeName.Contains("TDE_I")
                        || myblock.typeName.Contains("LE") || myblock.typeName.Contains("LT") || myblock.typeName.Contains("TO")|| myblock.typeName.Contains("STFB")|| myblock.typeName.Contains("EQ")|| myblock.typeName.Contains("NE"))
                    {
                        FBDList.Add(myblock.MyXML);
                    }
                }
            }
        }

        private static void ChangeXMLheader(ref XElement myprogamxml)
        {
            var postionlist = (from S in myprogamxml.Descendants()
                               where S.Name.LocalName == "LD"
                               select S).ToList();
            if (postionlist.Count > 0)
            {
                XElement thisLD = postionlist.First();

                if (thisLD != null)
                {
                    thisLD.Name = "FBD";
                }
            }
        }

        public static void ChangeContactsToVariable(ref SafetyTemplate SafetyTemplate)
        {
            foreach (contact mycon in SafetyTemplate.SafetycontactMap.Values)
            {
                contact currentcont = mycon;
                ChangeContactToVariable(ref currentcont);
            }
        }

        private static void ChangeContactToVariable(ref contact currentcont)
        {
            XElement ioVariable = currentcont.MyXML;
            ioVariable.Attribute("width").Value = CoilContactWidth;
            ioVariable.Attribute("height").Value = CoilContactHeight;
            currentcont.width = CoilContactWidth;
            currentcont.height = CoilContactHeight;
            ioVariable.Name = "inOutVariable";
            var variablelist = (from N in ioVariable.Descendants()
                                where N.Name.LocalName == "variable"
                                select N).ToList();
            if (variablelist.Count > 0)
            {
                XElement thisvariable = variablelist[0];
                thisvariable.Name = "expression";
            }
            XElement discp = ioVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "ANNOTATEMONITORVALUE");
            if (discp != null)
            {
                discp.Value = "true";
            }
            discp = ioVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "ANNOTATE");
            if (discp != null)
            {
                discp.Value = "true";
            }
            relPosition myrelposition = currentcont.connectionPointInMap.relPositionMap.FirstOrDefault();
            changeRelPosition(ref myrelposition, "-1", "1");
            myrelposition = currentcont.connectionPointOutMap.relPositionMap.FirstOrDefault();
            changeRelPosition(ref myrelposition, (int.Parse(currentcont.width) + 1).ToString(), "1");
        }

        private static void changeRelPosition(ref relPosition myrelposition, string x, string y)
        {
            myrelposition.MyXML.Attribute("x").Value = x;
            myrelposition.MyXML.Attribute("y").Value = y;
            myrelposition.X = x;
            myrelposition.Y = y;
        }

        public static void ChangeCoilsToVariable(ref SafetyTemplate SafetyTemplate)
        {
            foreach (coil mycon in SafetyTemplate.SafetycoilMap.Values)
            {
                coil currentcoil = mycon;
                ChangeCoilToVariable(ref currentcoil);
            }
        }

        private static void ChangeCoilToVariable(ref coil currentcoil)
        {
            XElement ioVariable = currentcoil.MyXML;
            ioVariable.Attribute("width").Value = CoilContactWidth;
            ioVariable.Attribute("height").Value = CoilContactHeight;
            currentcoil.width = CoilContactWidth;
            currentcoil.height = CoilContactHeight;
            if (ioVariable.Attribute("storage") != null)
            {
                ioVariable.Attribute("storage").Remove();
            }
            XElement discp = ioVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "ANNOTATEMONITORVALUE");
            if (discp != null)
            {
                discp.Value = "true";
            }
            discp = ioVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "ANNOTATE");
            if (discp != null)
            {
                discp.Value = "true";
            }

            ioVariable.Name = "inOutVariable";
            var variablelist = (from N in ioVariable.Descendants()
                                where N.Name.LocalName == "variable"
                                select N).ToList();
            if (variablelist.Count > 0)
            {
                XElement thisvariable = variablelist[0];
                thisvariable.Name = "expression";
            }
            relPosition myrelposition = currentcoil.connectionPointInMap.relPositionMap.FirstOrDefault();
            changeRelPosition(ref myrelposition, "-1", "1");
            myrelposition = currentcoil.connectionPointOutMap.relPositionMap.FirstOrDefault();
            changeRelPosition(ref myrelposition, "19", "1");
            connectCoilByFnBlock(ref currentcoil);
        }

        private static void connectCoilByFnBlock(ref coil mycoil)
        {
            if (mycoil.MyLogic.LogicBlockMap.Count > 0)
            {
                SafetyBlock myblock = mycoil.MyLogic.LogicBlockMap.FirstOrDefault().Value;
                string formalparm = mycoil.MyLogic.LogicBlockMap.FirstOrDefault().Key;
                if (formalparm.Contains("@"))
                {
                    formalparm = formalparm.Split('@')[1];
                    position mypostion = GetPostionParmeter(myblock, formalparm, 1, -1);
                    ChangeCoilPostion(ref mycoil, double.Parse(mypostion.X), double.Parse(mypostion.Y));
                }
            }
        }

        public static position GetPostionParmeter(SafetyBlock myblock, string formalparm, int offsetx, int offsety)
        {
            position myposition = new position();
            myposition.X = myblock.positionMap.X;
            myposition.Y = myblock.positionMap.Y;
            relPosition repostion = new relPosition();
            var test = myblock.outputVariablesMap.variableMap.FirstOrDefault(vr => vr.Value.formalParameter == formalparm).Value;
            if (test == null)
            {
                test = myblock.inputVariablesMap.variableMap.FirstOrDefault(vr => vr.Value.formalParameter == formalparm).Value;
                if (test != null)
                {
                    repostion = test.connectionPointInMap.relPositionMap.FirstOrDefault();
                }
            }
            else
            {
                repostion = test.connectionPointOutMap.relPositionMap.FirstOrDefault();
            }

            if (test != null)
            {
                if (repostion == null)//need check
                {
                    repostion = new relPosition();
                    repostion.X = "11";
                    repostion.Y = "4";
                }

                myposition.X = (double.Parse(myposition.X) + double.Parse(repostion.X) + offsetx).ToString();
                myposition.Y = (double.Parse(myposition.Y) + double.Parse(repostion.Y) + offsety).ToString();
            }

            myposition.MyXML.Attribute("x").Value = myposition.X;
            myposition.MyXML.Attribute("y").Value = myposition.Y;
            return myposition;
        }

        internal static void AddInputvariableToBlock(ref SafetyBlock motherBlock, string refconnection, string formalparameter, int inputcount)
        {
            variable myvar;
            int headhight = 2;
            if(!string.IsNullOrEmpty(motherBlock.instanceName))
            {
                headhight = 4;
            }
            try
            {
                if (inputcount > 2 && inputcount> motherBlock.inputVariablesMap.variableMap.Count())
                {
                    myvar = new variable(BlankBlock.inputVariablesMap.variableMap.FirstOrDefault().Value);

                    myvar.formalParameter = "IN" + inputcount.ToString();

                    myvar.MyXML.Attribute("formalParameter").Value = myvar.formalParameter.ToString();
                    int rely = headhight + (inputcount * 2);
                    ChangeYRePostion(ref myvar, rely);

                    //myvar.connectionPointInMap.relPositionMap.FirstOrDefault().MyXML.Attribute("y").Value = rely.ToString();
                }
                else
                {
                    // myvar = motherBlock.inputVariablesMap.variableMap["IN" + inputcount];
                    myvar = motherBlock.inputVariablesMap.variableMap.ToList()[inputcount - 1].Value;
                }
                //if(negated=="/")
                //{
                // //   myvar.MyXML.Add(new XAttribute("negated", "true"));
                //}
                ChangeReIDVariable(ref myvar, refconnection, formalparameter);
                if (inputcount > 2 && inputcount > motherBlock.inputVariablesMap.variableMap.Count())
                {
                    AddInpvariabletoBlock(ref motherBlock, myvar);
                }
            }
            catch (Exception ex)
            {
                string m = ex.Message;
            }
        }

        internal static void ChangeNegated(ref variable myvar, ref SafetyBlock myblock, string typeName)
        {
            if (typeName == "/")
            {
                string formalparm = myvar.formalParameter;
                var inpList = (from e in myblock.MyXML.Descendants()
                               where e.Name.LocalName == "inputVariables"
                               select e).FirstOrDefault();

                if (inpList != null)
                {
                    var varI = (from f in inpList.Descendants()
                                where f.Name.LocalName == "variable" && f.Attribute("formalParameter").Value == formalparm
                                select f).FirstOrDefault();
                    if (varI != null)
                    {
                        varI.Add(new XAttribute("negated", "true"));
                    }
                }
            }
        }

        private static void ChangeYRePostion(ref variable myvar, int rely)
        {
            myvar.connectionPointInMap.relPositionMap.FirstOrDefault().Y = rely.ToString();
            var connectionPointInlist = (from g in myvar.MyXML.Descendants()
                                         where g.Name.LocalName == "connectionPointIn"
                                         select g).ToList();
            if (connectionPointInlist.Count > 0)
            {
                var thispconnectionPointIn = connectionPointInlist[0];
                var thisrel = (from g in thispconnectionPointIn.Descendants()
                               where g.Name.LocalName == "relPosition"
                               select g).FirstOrDefault();

                thisrel.Attribute("y").Value = rely.ToString();
            }
        }

        private static void AddInpvariabletoBlock(ref SafetyBlock motherBlock, variable myvar)
        {
            motherBlock.inputVariablesMap.variableMap.Add(myvar.formalParameter, myvar);
            var inpList = (from e in motherBlock.MyXML.Descendants()
                           where e.Name.LocalName == "inputVariables"
                           select e).FirstOrDefault();

            if (inpList != null)
            {
                inpList.Add(myvar.MyXML);
            }
        }

        public static void ChangeReIDVariable(ref variable myvar, string refconnection, string formalparameter)
        {
            try
            {
                myvar.connectionPointInMap.connectionMap.refContactName = refconnection;
                myvar.connectionPointInMap.connectionMap.refLocalId = refconnection;
                myvar.connectionPointInMap.connectionMap.MyXML.Attribute("refLocalId").Value = refconnection;

                var connectionPointInlist = (from g in myvar.MyXML.Descendants()
                                             where g.Name.LocalName == "connectionPointIn"
                                             select g).ToList();

                if (connectionPointInlist.Count > 0)
                {
                    XElement thispconnectionPointIn = connectionPointInlist[0];

                    var connectionlist = (from L in thispconnectionPointIn.Descendants()
                                          where L.Name.LocalName == "connection"
                                          select L).ToList();
                    if (connectionlist.Count > 0)
                    {
                        XElement thisconnection = connectionlist[0];

                        thisconnection.Attribute("refLocalId").Value = refconnection;
                        if (formalparameter != "" && formalparameter != null)
                        {
                            myvar.connectionPointInMap.connectionMap.MyXML.Attribute("formalParameter").Value = formalparameter;
                            thisconnection.Attribute("formalParameter").Value = formalparameter;
                            myvar.connectionPointInMap.connectionMap.formalParameter = formalparameter;
                        }
                        else
                        {
                            if (myvar.connectionPointInMap.connectionMap.MyXML.Attribute("formalParameter") != null)
                            {
                                myvar.connectionPointInMap.connectionMap.MyXML.Attribute("formalParameter").Remove();
                                thisconnection.Attribute("formalParameter").Remove();
                                myvar.connectionPointInMap.connectionMap.formalParameter = "";
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                var tt = ex.Message;
            }
           
        }

        private static void ChangeCoilPostion(ref coil mycoil, double X, double Y)
        {
            mycoil.positionMap.MyXML.Attribute("x").Value = X.ToString();
            mycoil.positionMap.MyXML.Attribute("y").Value = Y.ToString();
            mycoil.positionMap.X = X.ToString();
            mycoil.positionMap.Y = Y.ToString();
        }

        private static void ReadCoil(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement ioFlgList, ref SortedDictionary<int, int> myIDs)
        {
            var contactList = from d in ioFlgList.Descendants()
                              where d.Name.LocalName == "coil"
                              select d;

            foreach (XElement thiscoil in contactList)
            {
                coil newcoil = ProgramOperations_Safety.Deserializecoil(thiscoil);
                myIDs.Add(int.Parse(newcoil.localId), int.Parse(newcoil.localId));
                newSafetyTemplateMap[newcoil.positionMap.SheetNumber].SafetycoilMap.Add(newcoil.localId, newcoil);
            }
        }

        private static void ReadContact(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement ioFlgList, ref SortedDictionary<int, int> myIDs)
        {
            var contactList = from d in ioFlgList.Descendants()
                              where d.Name.LocalName == "contact"
                              select d;

            foreach (XElement thiscontact in contactList)
            {
                contact newcontact = ProgramOperations_Safety.Deserializecontact(thiscontact);
                myIDs.Add(int.Parse(newcontact.localId), int.Parse(newcontact.localId));
                newSafetyTemplateMap[newcontact.positionMap.SheetNumber].SafetycontactMap.Add(newcontact.localId, newcontact);
            }
        }

        private static void ReadLoacalVariable(ref program newProgram)
        {
            foreach (SafetyTemplate sheet in newProgram.SafetyTemplateMap.Values)
            {
                foreach (inOutVariable varible in sheet.inOutVariableMap.Values)
                {
                    var locTag = newProgram.VariableMap.FirstOrDefault(val => val.Key == varible.expressionMap.Tagname).Value;
                    if (locTag == null)
                    {
                        if (varible.VariableType == "Local_Varaible")
                        {
                            newProgram.VariableMap.Add(varible.expressionMap.Tagname, varible);
                        }
                    }

                    var globalTag = globalVariableMap.FirstOrDefault(val => val.Key == varible.expressionMap.Tagname).Value;
                    if (globalTag == null)
                    {
                        if (varible.VariableType == "Global_Variable")
                        {
                            globalVariableMap.Add(varible.expressionMap.Tagname, varible);
                        }
                    }
                }
            }
        }

        internal static Comment newComment(string sheetNumber)
        {
            Comment newcomment = new Comment(Readcomment());

            XMLOperations_safety.ModifyCommentPosition(ref newcomment, sheetNumber);
            return newcomment;
        }

        private static Comment Readcomment()
        {
            XElement blankblock = FileWorks.LoadXMLFile(BlankfullPath);
            Comment newcomment = new Comment();
            var Comments = from c in blankblock.Descendants()
                           where c.Name.LocalName == "comment"
                           select c;
            if (Comments != null)
            {
                var firstcomment = Comments.FirstOrDefault();
                var textsize = (from c in firstcomment.Descendants()
                               where c.Name.LocalName == "TEXTSIZE"
                               select c).FirstOrDefault();
                textsize.Value = CommentTextSize;
                newcomment = ProgramOperations_Safety.DeserializeSafetyComment(firstcomment);
            }
            return newcomment;
        }

        private static void ReadComment(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement ioFlgList, ref SortedDictionary<int, int> myIDs)
        {
            var ioFlgList2 = from c in ioFlgList.Descendants()
                             where c.Name.LocalName == "comment"
                             select c;
            //read inoutvariable
            foreach (XElement thisIO in ioFlgList2)
            {
                Comment newcomment = ProgramOperations_Safety.DeserializeSafetyComment(thisIO);

                myIDs.Add(int.Parse(newcomment.localId), int.Parse(newcomment.localId));

                newSafetyTemplateMap[newcomment.SheetNumber].CommentMap.Add(newcomment.localId, newcomment);
            }
        }

        private static void ReadIOvariable(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement ioFlgList, ref SortedDictionary<int, int> myIDs)
        {
            var ioFlgList2 = from c in ioFlgList.Descendants()
                             where c.Name.LocalName == "inOutVariable" || c.Name.LocalName == "inVariable"
                             select c;
            //read inoutvariable
            foreach (XElement thisIO in ioFlgList2)
            {
                inOutVariable newinOutVariable = ProgramOperations_Safety.DeserializeSafetyinOutVariable(thisIO);

                myIDs.Add(int.Parse(newinOutVariable.localId), int.Parse(newinOutVariable.localId));
                newSafetyTemplateMap[newinOutVariable.positionMap.SheetNumber].inOutVariableMap.Add(newinOutVariable.localId, newinOutVariable);
            }
        }

        private static void ReadBlocks(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement ioFlgList, ref SortedDictionary<int, int> myIDs)
        {
            var blkList = from d in ioFlgList.Descendants()
                          where d.Name.LocalName == "block"
                          select d;
            //read blocks
            foreach (XElement thisBlk in blkList)
            {
                SafetyBlock newBlk = ProgramOperations_Safety.DeserializeSafetyBlock(thisBlk);
                myIDs.Add(int.Parse(newBlk.localId), int.Parse(newBlk.localId));
                newSafetyTemplateMap[newBlk.positionMap.SheetNumber].SafetyBlockMap.Add(newBlk.localId, newBlk);
            }
        }

        private static void ReadTagsConf(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement _GVList)
        {
            foreach (SafetyTemplate thisSafetyTemplate in newSafetyTemplateMap.Values)
            {
                foreach (inOutVariable thisinOutVariable in thisSafetyTemplate.inOutVariableMap.Values)
                {
                    thisinOutVariable.TagAttributesMap.TemplateName = thisSafetyTemplate.Template;
                    string variablename = thisinOutVariable.expressionMap.Tagname;

                    //determine local or gv
                    if (thisinOutVariable.VariableType == "Global_Variable or Local_Varaible")
                    {
                        XElement LOCdiscp;

                        var LOCinfos = (from b in _GVList.Descendants()
                                        where b.Name.LocalName == "pou" && b.ToString().Contains(thisSafetyTemplate.ProgramName)
                                        select b).FirstOrDefault();

                        var LOCvariableList = (from HH in LOCinfos.Descendants()
                                               where HH.Name.LocalName == "localVars" && !(HH.ToString().Contains("Constants"))
                                               select HH).FirstOrDefault();

                        var variableList = (from VV in LOCvariableList.Descendants()
                                            where VV.Name.LocalName == "variable" && VV.Attribute("name").Value == variablename
                                            select VV).FirstOrDefault();

                        if (variableList != null)
                        {
                            // LOCdiscp = LOCvariableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                            LOCdiscp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "type");
                            thisinOutVariable.TagAttributesMap.DATATYPE = LOCdiscp.Descendants().FirstOrDefault().Name.LocalName;

                            LOCdiscp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                            thisinOutVariable.TagAttributesMap.DESCRIPTION = LOCdiscp.Value;

                            thisinOutVariable.VariableType = "Local_Varaible";
                            thisinOutVariable.TagAttributesMap.VariablType = "Local_Varaible";
                            thisinOutVariable.MyInterfaceXML = variableList;
                        }
                        else
                        {
                            thisinOutVariable.VariableType = "Global_Variable";
                            thisinOutVariable.TagAttributesMap.VariablType = "Global_Variable";
                            var globalvariableList = (from HH in LOCinfos.Descendants()
                                                      where HH.Name.LocalName == "globalVars" && !(HH.ToString().Contains("Constants"))
                                                      select HH).FirstOrDefault();

                            var gvariableList = (from VV in globalvariableList.Descendants()
                                                 where VV.Name.LocalName == "variable" && VV.Attribute("name").Value == variablename
                                                 select VV).FirstOrDefault();
                            thisinOutVariable.MyInterfaceXML = gvariableList;
                        }
                    }

                    //end determine

                    if (thisinOutVariable.VariableType == "Global_Variable")
                    {
                        var infos = (from b in _GVList.Descendants()
                                     where b.Name.LocalName == "configurations"
                                     select b).FirstOrDefault();
                        //var variableList = (from HH in infos.Descendants()
                        //                    where HH.Name.LocalName == "variable" && HH.ToString().Contains(variablename)
                        //                    select HH).FirstOrDefault();
                        var variableList = (from HH in infos.Descendants()
                                            where HH.Name.LocalName == "variable" && HH.Attribute("name").Value == variablename
                                            select HH).FirstOrDefault();

                        //foreach (XElement thisvariable in variableList)
                        //{
                        XElement discp;
                        //  string variablename = thisvariable.Attribute("name").Value;
                        if (variableList != null)
                        {
                            thisinOutVariable.TagAttributesMap.Tagname = variablename;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                            thisinOutVariable.TagAttributesMap.DESCRIPTION = discp.Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "type"); ;
                            thisinOutVariable.TagAttributesMap.DATATYPE = discp.Descendants().FirstOrDefault().Name.LocalName;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DECLARATION");
                            thisinOutVariable.TagAttributesMap.GROUP2 = discp.Attribute("Group2").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DECLARATION");
                            thisinOutVariable.TagAttributesMap.GROUP1 = discp.Attribute("Group1").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "POINT_ASSIGNMENT");
                            thisinOutVariable.TagAttributesMap.ALIASTYPE = discp.Attribute("AliasType").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "POINT_ASSIGNMENT");
                            thisinOutVariable.TagAttributesMap.POINTTYPE = discp.Attribute("PointType").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "POINT_ASSIGNMENT");
                            string PhysicalAddress = discp.Attribute("PhysicalAddress").Value;
                            string[] address = PhysicalAddress.Split('.');

                            if (thisinOutVariable.TagAttributesMap.POINTTYPE != "MEMORY" & (address.Length > 2))
                            {
                                thisinOutVariable.TagAttributesMap.CHASSIS = address[0];
                                thisinOutVariable.TagAttributesMap.SLOT = address[1];
                                thisinOutVariable.TagAttributesMap.POINT = address[2];
                            }
                            else
                            {
                                thisinOutVariable.TagAttributesMap.CHASSIS = "";
                                thisinOutVariable.TagAttributesMap.SLOT = "";
                                thisinOutVariable.TagAttributesMap.POINT = "";
                            }

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DISPLAY");
                            thisinOutVariable.TagAttributesMap.FALSECOLOR = discp.Attribute("FalseStateColor").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DISPLAY");
                            thisinOutVariable.TagAttributesMap.TRUECOLOR = discp.Attribute("TrueStateColor").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DISPLAY");
                            thisinOutVariable.TagAttributesMap.FALSESTATE = discp.Attribute("FalseStateName").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DISPLAY");
                            thisinOutVariable.TagAttributesMap.TRUESTATE = discp.Attribute("TrueStateName").Value;

                            if ((thisinOutVariable.TagAttributesMap.POINTTYPE == "INPUT") & (thisinOutVariable.TagAttributesMap.DATATYPE == "DINT"))
                            {
                                thisinOutVariable.TagAttributesMap.SignalType = "AI";
                            }

                            if ((thisinOutVariable.TagAttributesMap.POINTTYPE == "OUTPUT") & (thisinOutVariable.TagAttributesMap.DATATYPE == "DINT"))
                            {
                                thisinOutVariable.TagAttributesMap.SignalType = "AO";
                            }

                            if ((thisinOutVariable.TagAttributesMap.POINTTYPE == "OUTPUT") & (thisinOutVariable.TagAttributesMap.DATATYPE == "BOOL"))
                            {
                                thisinOutVariable.TagAttributesMap.SignalType = "DO";
                            }

                            if ((thisinOutVariable.TagAttributesMap.POINTTYPE == "INPUT") & (thisinOutVariable.TagAttributesMap.DATATYPE == "BOOL"))
                            {
                                thisinOutVariable.TagAttributesMap.SignalType = "DI";
                            }
                        }
                        //}
                    }
                }
            }
        }

        private static void ModifyIOvariablePosition(ref inOutVariable modinOutVariable, string newSheetNumber)
        {
            XElement ioVariable = modinOutVariable.MyXML;

            var postionlist = from S in ioVariable.Descendants()
                              where S.Name.LocalName == "position" && S.ToString().Contains("SheetNumber")
                              select S;
            XElement thispostion = postionlist.First();

            if (thispostion != null)
            {
                modinOutVariable.positionMap.SheetNumber = newSheetNumber;
                thispostion.LastAttribute.Value = newSheetNumber;//ToDo:there is problem to get attribute contain :
            }
        }

        // static void ModifyIOvariableName(ref inOutVariable modinOutVariable,string OldTag, string newTag,XElement modinterface)
        private static void ModifyIOvariableName(ref inOutVariable modinOutVariable, string OldTag, string newTag)
        {
            XElement ioVariable = modinOutVariable.MyXML;
            XElement ioInterfaceVariable = modinOutVariable.MyInterfaceXML;
            XElement ioGlobalConfigVariable = modinOutVariable.MyGlobalConfigXML;

            if (ioVariable != null)
            {
                modinOutVariable.expressionMap.Tagname = newTag;
                modinOutVariable.TagAttributesMap.Tagname = newTag;

                var expressionlist = (from N in ioVariable.Descendants()
                                      where N.Name.LocalName == "expression"
                                      select N).ToList();
                if (expressionlist.Count > 0)
                {
                    XElement thisexpression = expressionlist[0];
                    thisexpression.Value = newTag;
                }
            }
            if (ioInterfaceVariable != null)
            {
                ioInterfaceVariable.Attribute("name").Value = newTag;
            }
            if (ioGlobalConfigVariable != null)
            {
                ioGlobalConfigVariable.Attribute("name").Value = newTag;
            }
        }

        private static void ModifyBlockPosition(ref SafetyBlock modSafetyBlock, string newSheetNumber)
        {
            XElement xmlSafetyBlock = modSafetyBlock.MyXML;

            var postionlist = from S in xmlSafetyBlock.Descendants()
                              where S.Name.LocalName == "position" && S.ToString().Contains(":SheetNumber")
                              select S;
            XElement thispostion = postionlist.First();

            if (thispostion != null)
            {
                modSafetyBlock.positionMap.SheetNumber = newSheetNumber;
                thispostion.LastAttribute.Value = newSheetNumber;//ToDo:there is problem to get attribute contain :
            }
        }

        private static void ModifyCommentPosition(ref Comment modSafetyComment, string newSheetNumber)
        {
            XElement xmlSafetycomment = modSafetyComment.MyXML;

            var postionlist = from S in xmlSafetycomment.Descendants()
                              where S.Name.LocalName == "position" && S.ToString().Contains(":SheetNumber")
                              select S;
            XElement thispostion = postionlist.First();

            if (thispostion != null)
            {
                modSafetyComment.SheetNumber = newSheetNumber;
                thispostion.LastAttribute.Value = newSheetNumber;//ToDo:there is problem to get attribute contain :
            }
        }

        public static void ModifySafetTemplatePosition(ref SafetyTemplate modSafetyTemplate, string newSheetNumber)
        {
            XElement thissheet = modSafetyTemplate.MyXMLSheet;

            thissheet.Attribute("Number").Value = newSheetNumber;
            modSafetyTemplate.SheetNumber = newSheetNumber;

            foreach (inOutVariable modinoutvar in modSafetyTemplate.inOutVariableMap.Values)
            {
                inOutVariable testio = modinoutvar;
                XMLOperations_LadderLogic.ModifyIOvariablePosition(ref testio, newSheetNumber);
            }

            foreach (SafetyBlock modblock in modSafetyTemplate.SafetyBlockMap.Values)
            {
                SafetyBlock testblock = modblock;
                XMLOperations_LadderLogic.ModifyBlockPosition(ref testblock, newSheetNumber);
            }

            foreach (Comment modcomment in modSafetyTemplate.CommentMap.Values)
            {
                Comment testcomment = modcomment;
                XMLOperations_LadderLogic.ModifyCommentPosition(ref testcomment, newSheetNumber);
            }
        }

        //static void ModifySafetTemplateTag(ref SafetyTemplate modSafetyTemplate, string OldTag, string newTag,ref program modprogram)
        public static void ModifySafetyTemplateTag(ref SafetyTemplate modSafetyTemplate, string OldTag, string newTag)
        {
            //XElement thissheet = modSafetyTemplate.MyXMLSheet;

            //thissheet.Attribute("Number").Value = newSheetNumber;
            //modSafetyTemplate.SheetNumber = newSheetNumber;

            foreach (inOutVariable modinoutvar in modSafetyTemplate.inOutVariableMap.Values)
            {
                inOutVariable testio = modinoutvar;
                if (testio.expressionMap.Tagname == OldTag)
                {
                    if (testio.VariableType == "Global_Variable")
                    {
                        //  XMLOperations_LadderLogic.ModifyIOvariableName(ref testio, OldTag, newTag, modprogram.globalVarsXML);
                        XMLOperations_LadderLogic.ModifyIOvariableName(ref testio, OldTag, newTag);
                    }

                    if (testio.VariableType == "Local_Varaible")
                    {
                        //XMLOperations_LadderLogic.ModifyIOvariableName(ref testio, OldTag, newTag, modprogram.localVarsXML);
                        XMLOperations_LadderLogic.ModifyIOvariableName(ref testio, OldTag, newTag);
                    }
                }
            }

            //foreach (SafetyBlock modblock in modSafetyTemplate.SafetyBlockMap.Values)
            //{
            //    SafetyBlock testblock = modblock;
            //    XMLOperations_LadderLogic.ModifyBlockTName(ref testblock, newSheetNumber);
            //}
        }

        public SafetyTemplate DuplicateSafetyTemplate(SafetyTemplate modSafetytemplate, SortedDictionary<int, int> IDS)
        {
            SafetyTemplate newSafetyTemplatey = new SafetyTemplate(modSafetytemplate);
            //SafetyTemplate newSafetyTemplatey = modSafetytemplate;

            foreach (inOutVariable inoutvar in newSafetyTemplatey.inOutVariableMap.Values)
            {
                inOutVariable testinoutvar = inoutvar;
                string oldID = testinoutvar.localId;
                //string newID = (IDS[IDS.Count() - 1]+1).ToString();
                string newID = (IDS.ElementAt(IDS.Count() - 1).Value + 1).ToString();
                IDS.Add(int.Parse(newID), int.Parse(newID));
                newSafetyTemplatey = ModifyID(newSafetyTemplatey, oldID, newID);
            }
            return newSafetyTemplatey;
        }

        private SafetyTemplate ModifyID(SafetyTemplate modSafetyTemplate, string oldID, string newTagID)
        {
            XElement ioVariableXml = null;
            SafetyTemplate newSafetyTemplate = new SafetyTemplate(modSafetyTemplate);
            var ioVariablevalue = newSafetyTemplate.inOutVariableMap.FirstOrDefault(vr => vr.Value.localId == oldID).Value;
            if (ioVariablevalue != null)
            {
                ioVariableXml = ioVariablevalue.MyXML;
            }
            inOutVariable ioVariable = newSafetyTemplate.inOutVariableMap.FirstOrDefault(vr => vr.Value.localId == oldID).Value;

            if (ioVariableXml != null)
            {
                ioVariableXml.Attribute("localId").Value = newTagID;

                ioVariable.localId = newTagID;
                foreach (SafetyBlock bloks in newSafetyTemplate.SafetyBlockMap.Values)
                {
                    if (bloks.MyXML.ToString().Contains(oldID))
                    {
                        //Enumerable<variable> listvar = bloks.inputVariablesMap.variableMap.Values.SelectMany(vr => vr.connectionPointInMap.connectionMap.refLocalId == oldID).ToList;
                        foreach (variable foundVar in bloks.inputVariablesMap.variableMap.Values)
                        {
                            if (foundVar.connectionPointInMap.connectionMap.refLocalId == oldID)
                            {
                                foundVar.connectionPointInMap.connectionMap.refLocalId = newTagID;
                                foundVar.connectionPointInMap.connectionMap.MyXML.Attribute("refLocalId").Value = newTagID;
                            }
                        }
                    }
                }
            }

            return newSafetyTemplate;
        }

        public static void ModifySafetyTemplateTitle(ref SafetyTemplate test2, string newIOName)
        {
            var macroList = from b in test2.MyXMLSheet.Descendants()
                            where b.Name.LocalName == "Macros"
                            select b;

            foreach (XElement thismacro in macroList)
            {
                string macroname = thismacro.Attribute("Name").Value;
                if (macroname == "SHEET_TITLE")
                {
                    var valuesheettitle = (from d in thismacro.Descendants()
                                           where d.Name.LocalName == "Value"
                                           select d).ToList();
                    if (valuesheettitle.Count > 0)
                    {
                        XElement thisvalues = valuesheettitle[0];
                        //string sheettemplate = thisvalues.Value;
                        thisvalues.Value = newIOName;
                    }
                }
            }
            test2.Name = newIOName;

            test2.Template = newIOName;
            foreach (SafetyBlock modblock in test2.SafetyBlockMap.Values)
            {
                modblock.BlockAttributes.TemplateName = newIOName;
            }
            foreach (inOutVariable modIO in test2.inOutVariableMap.Values)
            {
                modIO.TagAttributesMap.TemplateName = newIOName;
            }
        }

        public static void ModifySafetyBlockName(ref SafetyTemplate test2, string oldIOName, string newIOName)
        {
            SafetyBlock modblock = test2.SafetyBlockMap.FirstOrDefault(blk => blk.Value.instanceName == oldIOName).Value;
            if (modblock != null)
            {
                modblock.instanceName = newIOName;
                modblock.BlockAttributes.BlockName = newIOName;

                if (modblock.MyXML.ToString().Contains("instanceName"))
                {
                    modblock.MyXML.Attribute("instanceName").Value = newIOName;
                }
            }
        }

        internal static void ModifySafetyTemplateTagDescription(ref SafetyTemplate modSafetyTemplate, string IODeclerationName, string newIODeclerationDescription)
        {
            foreach (inOutVariable modinoutvar in modSafetyTemplate.inOutVariableMap.Values)
            {
                inOutVariable testio = modinoutvar;
                if (testio.expressionMap.Tagname == IODeclerationName)
                {
                    if (testio.VariableType == "Global_Variable")
                    {
                        XMLOperations_LadderLogic.ModifyIOvariableDescription(ref testio, IODeclerationName, newIODeclerationDescription);
                    }

                    if (testio.VariableType == "Local_Varaible")
                    {
                        XMLOperations_LadderLogic.ModifyIOvariableDescription(ref testio, IODeclerationName, newIODeclerationDescription);
                    }
                }
            }
        }

        public static void ModifyIOvariableDescription(ref inOutVariable modinOutVariable, string IODeclerationName, string newIODeclerationDescription)
        {
            XElement ioVariable = modinOutVariable.MyXML;
            XElement ioInterfaceVariable = modinOutVariable.MyInterfaceXML;
            XElement ioGlobalConfigVariable = modinOutVariable.MyGlobalConfigXML;

            if (ioInterfaceVariable != null)
            {
                var LOCdiscp = ioInterfaceVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                if (LOCdiscp != null)
                {
                    LOCdiscp.ReplaceNodes(new XCData(newIODeclerationDescription));

                    modinOutVariable.TagAttributesMap.DESCRIPTION = newIODeclerationDescription;
                }
            }
            if (ioGlobalConfigVariable != null)
            {
                var discp = ioGlobalConfigVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                discp.ReplaceNodes(new XCData(newIODeclerationDescription));

                modinOutVariable.TagAttributesMap.DESCRIPTION = newIODeclerationDescription;
            }
        }

        internal static void ModifySafetyTemplateCommentContent(ref SafetyTemplate modSafetyTemplate, string CommentID, string newCommentContent)
        {
            foreach (Comment modinoutvar in modSafetyTemplate.CommentMap.Values)
            {
                Comment testio = modinoutvar;
                if (testio.localId == CommentID)
                {
                    XMLOperations_LadderLogic.ModifyCommentContent(ref testio, newCommentContent);
                }
            }
        }

        private static void ModifyCommentContent(ref Comment ModComment, string newCommentContent)
        {
            XElement CommentXML = ModComment.MyXML;

            if (CommentXML != null)
            {
                var LOCComment = CommentXML.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "content");
                if (LOCComment != null)
                {
                    LOCComment.ReplaceNodes(new XCData(newCommentContent));
                    ModComment.content = newCommentContent;
                }
            }
        }

        internal static void ModifyProgramName(ref program Program, string newProgramName)
        {
            XElement programXML = Program.MyXML;
            XElement ExectutionXML = Program.ExecutionListXML;
            if (programXML != null)
            {
                Program.Name = newProgramName;
                programXML.Attribute("name").Value = newProgramName;
                if (ExectutionXML != null)
                {
                    ExectutionXML.Attribute("name").Value = newProgramName;
                }
            }
        }

        private static void ReadTagsConf(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement PrgXML, XElement configurationsXML)
        {
            foreach (SafetyTemplate thisSafetyTemplate in newSafetyTemplateMap.Values)
            {
                foreach (inOutVariable thisinOutVariable in thisSafetyTemplate.inOutVariableMap.Values)
                {
                    thisinOutVariable.TagAttributesMap.TemplateName = thisSafetyTemplate.Template;
                    string variablename = thisinOutVariable.expressionMap.Tagname;

                    //determine local or gv
                    if (thisinOutVariable.VariableType == "Global_Variable or Local_Varaible")
                    {
                        XElement LOCdiscp;

                        var LOCvariableList = (from HH in PrgXML.Descendants()
                                               where HH.Name.LocalName == "localVars" && !(HH.ToString().Contains("Constants"))
                                               select HH).FirstOrDefault();

                        var variableList = (from VV in LOCvariableList.Descendants()
                                            where VV.Name.LocalName == "variable" && VV.Attribute("name").Value == variablename
                                            select VV).FirstOrDefault();

                        if (variableList != null)
                        {
                            LOCdiscp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "type");
                            thisinOutVariable.TagAttributesMap.DATATYPE = LOCdiscp.Descendants().FirstOrDefault().Name.LocalName;

                            LOCdiscp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                            thisinOutVariable.TagAttributesMap.DESCRIPTION = LOCdiscp.Value;

                            thisinOutVariable.VariableType = "Local_Varaible";
                            thisinOutVariable.TagAttributesMap.VariablType = "Local_Varaible";
                            thisinOutVariable.MyInterfaceXML = variableList;
                        }
                        else
                        {
                            thisinOutVariable.VariableType = "Global_Variable";
                            thisinOutVariable.TagAttributesMap.VariablType = "Global_Variable";
                            var globalvariableList = (from HH in PrgXML.Descendants()
                                                      where HH.Name.LocalName == "globalVars" && !(HH.ToString().Contains("Constants"))
                                                      select HH).FirstOrDefault();

                            var gvariableList = (from VV in globalvariableList.Descendants()
                                                 where VV.Name.LocalName == "variable" && VV.Attribute("name").Value == variablename
                                                 select VV).FirstOrDefault();
                            thisinOutVariable.MyInterfaceXML = gvariableList;
                        }
                    }

                    if (thisinOutVariable.VariableType == "Global_Variable")
                    {
                        var variableList = (from HH in configurationsXML.Descendants()
                                            where HH.Name.LocalName == "variable" && HH.Attribute("name").Value == variablename
                                            select HH).FirstOrDefault();

                        XElement discp;

                        if (variableList != null)
                        {
                            thisinOutVariable.TagAttributesMap.Tagname = variablename;
                            thisinOutVariable.MyGlobalConfigXML = variableList;
                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                            thisinOutVariable.TagAttributesMap.DESCRIPTION = discp.Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "type"); ;
                            thisinOutVariable.TagAttributesMap.DATATYPE = discp.Descendants().FirstOrDefault().Name.LocalName;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DECLARATION");
                            thisinOutVariable.TagAttributesMap.GROUP2 = discp.Attribute("Group2").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DECLARATION");
                            thisinOutVariable.TagAttributesMap.GROUP1 = discp.Attribute("Group1").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "POINT_ASSIGNMENT");
                            thisinOutVariable.TagAttributesMap.ALIASTYPE = discp.Attribute("AliasType").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "POINT_ASSIGNMENT");
                            thisinOutVariable.TagAttributesMap.POINTTYPE = discp.Attribute("PointType").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "POINT_ASSIGNMENT");
                            string PhysicalAddress = discp.Attribute("PhysicalAddress").Value;
                            string[] address = PhysicalAddress.Split('.');

                            if (thisinOutVariable.TagAttributesMap.POINTTYPE != "MEMORY" & (address.Length > 2))
                            {
                                thisinOutVariable.TagAttributesMap.CHASSIS = address[0];
                                thisinOutVariable.TagAttributesMap.SLOT = address[1];
                                thisinOutVariable.TagAttributesMap.POINT = address[2];
                            }
                            else
                            {
                                thisinOutVariable.TagAttributesMap.CHASSIS = "";
                                thisinOutVariable.TagAttributesMap.SLOT = "";
                                thisinOutVariable.TagAttributesMap.POINT = "";
                            }

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DISPLAY");
                            thisinOutVariable.TagAttributesMap.FALSECOLOR = discp.Attribute("FalseStateColor").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DISPLAY");
                            thisinOutVariable.TagAttributesMap.TRUECOLOR = discp.Attribute("TrueStateColor").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DISPLAY");
                            thisinOutVariable.TagAttributesMap.FALSESTATE = discp.Attribute("FalseStateName").Value;

                            discp = variableList.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "TAG_DISPLAY");
                            thisinOutVariable.TagAttributesMap.TRUESTATE = discp.Attribute("TrueStateName").Value;

                            if ((thisinOutVariable.TagAttributesMap.POINTTYPE == "INPUT") & (thisinOutVariable.TagAttributesMap.DATATYPE == "DINT"))
                            {
                                thisinOutVariable.TagAttributesMap.SignalType = "AI";
                            }

                            if ((thisinOutVariable.TagAttributesMap.POINTTYPE == "OUTPUT") & (thisinOutVariable.TagAttributesMap.DATATYPE == "DINT"))
                            {
                                thisinOutVariable.TagAttributesMap.SignalType = "AO";
                            }

                            if ((thisinOutVariable.TagAttributesMap.POINTTYPE == "OUTPUT") & (thisinOutVariable.TagAttributesMap.DATATYPE == "BOOL"))
                            {
                                thisinOutVariable.TagAttributesMap.SignalType = "DO";
                            }

                            if ((thisinOutVariable.TagAttributesMap.POINTTYPE == "INPUT") & (thisinOutVariable.TagAttributesMap.DATATYPE == "BOOL"))
                            {
                                thisinOutVariable.TagAttributesMap.SignalType = "DI";
                            }
                        }
                    }
                }
            }
        }

        public static void ChangeBlockID(ref SafetyBlock myBlock, string newID)
        {
            myBlock.MyXML.Attribute("localId").Value = newID;

            myBlock.localId = newID;
        }

        internal static void DeserializeSafetyfnblock(List<string> library, ref XElement newProgram)
        {
            int i = 1;
            string fnname = "";
            foreach(var lib in library)
            {
                fnname= "STFB"+i.ToString();
               String  mylib = lib.Replace("STFB", fnname);
                XElement myprogrm = FileWorks.LoadXMLFile(BlankfullPath);
                var thisprogram = (from d in myprogrm.Descendants()
                                   where d.Name.LocalName == "pou" && d.Attribute("pouType").Value == "functionBlock"
                                   select d).FirstOrDefault();

                if (thisprogram != null)
                {
                    thisprogram.Attribute("name").Value = fnname;

                    var LOCST = thisprogram.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "ST");
                    if (LOCST != null)
                    {
                        LOCST.ReplaceNodes(new XCData(mylib));
                      
                    }
                }
                AddFBXmlToPT2(thisprogram, ref newProgram);
                i++;
            }

        }

        internal static void DeserializeSafetySTProgram(string mylib, ref XElement newProgram)
        {
          


               string fnname = "SetPoints";
               
                XElement myprogrm = FileWorks.LoadXMLFile(BlankfullPath);
                var thisprogram = (from d in myprogrm.Descendants()
                                   where d.Name.LocalName == "pou" && d.Attribute("pouType").Value == "functionBlock"
                                   select d).FirstOrDefault();

                if (thisprogram != null)
                {
                    thisprogram.Attribute("name").Value = fnname;
                   thisprogram.Attribute("pouType").Value = "program";


                var LOCST = thisprogram.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "ST");
                    if (LOCST != null)
                    {
                        LOCST.ReplaceNodes(new XCData(mylib));

                    }
                }
                AddFBXmlToPT2(thisprogram, ref newProgram);
               
        }

        private static void AddFBXmlToPT2(XElement thisprogram, ref XElement newProgram)
        {
            var pt2program = (from d in newProgram.Descendants()
                               where d.Name.LocalName == "pous" 
                               select d).FirstOrDefault();
            pt2program.AddFirst(thisprogram);
        }
        public static void delBlank()
        {
            if (System.IO.File.Exists(BlankPT2fullPath))
            {
                System.IO.File.Delete(BlankPT2fullPath);
            }
           
            if (System.IO.File.Exists(BlankfullPath))
            {
                System.IO.File.Delete(BlankfullPath);
            }
        }
    }
}