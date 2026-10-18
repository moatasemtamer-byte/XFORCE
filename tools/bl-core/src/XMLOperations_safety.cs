/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 *
 * Purpose:             XML deserialization and (later also) serialization of StrategyTemplates.
 *
 *
 * DATE        Author   Description
 * --------    ------   ---------------------------------------------------------
 *
 ********************************************************************************************/

using BL.SafetyData;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System;

namespace BL
{
    public class XMLOperations_safety
    {
        public static Dictionary<string, inOutVariable> globalVariableMap;

        public static List<program> DeserializeSafetyProgram(XElement loadedXml)
        {
            var ioFlgList = (from b in loadedXml.Descendants()
                             where b.Name.LocalName == "pou" && b.Attribute("pouType").Value == "program"
                             select b).ToList();

            var configurations = (from b in loadedXml.Descendants()
                                  where b.Name.LocalName == "configurations"
                                  select b).FirstOrDefault();

            List<program> newPrograms = new List<program>();
            XMLOperations_safety.globalVariableMap = new Dictionary<string, inOutVariable>();
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
                    Dictionary<string, SafetyTemplate> newSafetyTemplateMap = ProgramOperations_Safety.DeserializeSafetyTemplate(PrgInioFlgList, newProgram.Name);
                    SortedDictionary<int, int> IDS = new SortedDictionary<int, int>();

                    ReadIOvariable(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
                    ReadContact(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
                    ReadCoil(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
                    ReadBlocks(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
                    ReadCoillogic(ref newSafetyTemplateMap);
                    XElement railist = PrgInioFlgList;
                    RemoveRails(ref railist);

                    //test
                    XElement myproxml = PrgInioFlgList;
                    foreach (SafetyTemplate ss in newSafetyTemplateMap.Values)
                    {
                        SafetyTemplate vv = ss;

                        XMLOperations_LadderLogic.ChangeLadderElementsToFBD(ref vv, ref myproxml);
                    }
                    List<string> mismatches = new List<string>();
                    XMLOperations_LadderLogic.BuildCoillogic(ref newSafetyTemplateMap, ref IDS, ref myproxml, mismatches);

                    //test
                    // ReadTagsConf(ref newSafetyTemplateMap, loadedXml);
                    ReadTagsConf(ref newSafetyTemplateMap, PrgInioFlgList, configurations);

                    ReadComment(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);

                    newProgram.IDsMap = IDS;
                    newProgram.SafetyTemplateMap = newSafetyTemplateMap;
                    ReadLoacalVariable(ref newProgram);
                    newPrograms.Add(newProgram);
                }
            }
            // loadedXml.Save("D:\\ladder.xml");
            return newPrograms;
        }

        private static void RemoveRails(ref XElement prgInioFlgList)
        {
            var RailsList = from d in prgInioFlgList.Descendants()
                            where d.Name.LocalName == "rightPowerRail" || d.Name.LocalName == "leftPowerRail"
                            select d;
            if (RailsList != null)
            {
                RailsList.Remove();
            }
        }

        private static void ReadCoillogic(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap)
        {
            foreach (SafetyTemplate st in newSafetyTemplateMap.Values)
            {
                SafetyTemplate currentst = st;

                ProgramOperations_Safety.GetLogic(ref currentst);
            }
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

        public static void ModifyIOvariablePosition(ref inOutVariable modinOutVariable, string newSheetNumber)
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
        public static void ModifyIOvariableName(ref inOutVariable modinOutVariable, string OldTag, string newTag)
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

        public static void ModifyBlockPosition(ref SafetyBlock modSafetyBlock, string newSheetNumber)
        {
            if (newSheetNumber!=null)
                {
                    XElement xmlSafetyBlock = modSafetyBlock.MyXML;

                    var postionlist = from S in xmlSafetyBlock.Descendants()
                                      where S.Name.LocalName == "position" && S.ToString().Contains(":SheetNumber")//:sheetnumber
                                      select S;
                    XElement thispostion = postionlist.First();

                    if (thispostion != null)
                    {
                        modSafetyBlock.positionMap.SheetNumber = newSheetNumber;
                        thispostion.LastAttribute.Value = newSheetNumber;//ToDo:there is problem to get attribute contain :
                    } 
                }
          
        }

       public static void ModifyCommentPosition(ref Comment modSafetyComment, string newSheetNumber)
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
                XMLOperations_safety.ModifyIOvariablePosition(ref testio, newSheetNumber);
            }

            foreach (SafetyBlock modblock in modSafetyTemplate.SafetyBlockMap.Values)
            {
                SafetyBlock testblock = modblock;
                XMLOperations_safety.ModifyBlockPosition(ref testblock, newSheetNumber);
            }

            foreach (Comment modcomment in modSafetyTemplate.CommentMap.Values)
            {
                Comment testcomment = modcomment;
                XMLOperations_safety.ModifyCommentPosition(ref testcomment, newSheetNumber);
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
                        //  XMLOperations_safety.ModifyIOvariableName(ref testio, OldTag, newTag, modprogram.globalVarsXML);
                        XMLOperations_safety.ModifyIOvariableName(ref testio, OldTag, newTag);
                    }

                    if (testio.VariableType == "Local_Varaible")
                    {
                        //XMLOperations_safety.ModifyIOvariableName(ref testio, OldTag, newTag, modprogram.localVarsXML);
                        XMLOperations_safety.ModifyIOvariableName(ref testio, OldTag, newTag);
                    }
                }
            }

            //foreach (SafetyBlock modblock in modSafetyTemplate.SafetyBlockMap.Values)
            //{
            //    SafetyBlock testblock = modblock;
            //    XMLOperations_safety.ModifyBlockTName(ref testblock, newSheetNumber);
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

        public static void ModifyIOvariableID(ref inOutVariable myvariable, string newTagID)
        {
            XElement ioVariableXml = null;

            if (myvariable != null)
            {
                ioVariableXml = myvariable.MyXML;
            }

            if (ioVariableXml != null)
            {
                ioVariableXml.Attribute("localId").Value = newTagID;

                myvariable.localId = newTagID;
            }
        }

        internal static void ModifyCommentHieght(ref Comment mycomment, int commenthieght)
        {
            XElement commentXml = null;
            if (mycomment != null)
            {
                commentXml = mycomment.MyXML;

            }

            if (commentXml != null)
            {
                commentXml.Attribute("height").Value = commenthieght.ToString();

               // mycomment.localId = newTagID;
            }
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
                ModifyBlockName(newIOName, modblock);
            }
        }

        public static void ModifyBlockName(string newIOName, SafetyBlock modblock)
        {
            modblock.instanceName = newIOName;
            modblock.BlockAttributes.BlockName = newIOName;

            if (modblock.MyXML.ToString().Contains("instanceName"))
            {
                modblock.MyXML.Attribute("instanceName").Value = newIOName;
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
                        XMLOperations_safety.ModifyIOvariableDescription(ref testio, IODeclerationName, newIODeclerationDescription);
                    }

                    if (testio.VariableType == "Local_Varaible")
                    {
                        XMLOperations_safety.ModifyIOvariableDescription(ref testio, IODeclerationName, newIODeclerationDescription);
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
                    XMLOperations_safety.ModifyCommentContent(ref testio, newCommentContent);
                }
            }
        }

       public static void ModifyCommentContent(ref Comment ModComment, string newCommentContent)
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
                    else
                    {
                        UpdateConstantInterface(thisinOutVariable, PrgXML);
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

        private static void UpdateConstantInterface(inOutVariable thisinOutVariable, XElement progxml)
        {
            var variablename = thisinOutVariable.expressionMap.Tagname;



            var LOCvariableList = (from HH in progxml.Descendants()
                                   where HH.Name.LocalName == "localVars" && (HH.ToString().Contains("Constants"))
                                   select HH).FirstOrDefault();

            var variableList = (from VV in LOCvariableList.Descendants()
                                where VV.Name.LocalName == "variable" && VV.Attribute("name").Value == variablename
                                select VV).FirstOrDefault();

            if (variableList != null)
            {

                thisinOutVariable.MyInterfaceXML = variableList;

            }

        }
    }
}