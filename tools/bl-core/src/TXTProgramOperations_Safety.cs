using BL.SafetyData;
using System;

/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 *
 * Purpose:             XML deserialization and (later also) serialization of StrategyTemplates.
 *
 *
 * Rev  Date            Author  Description
 * ---  ----------      ------  ---------------------------------------------------------
 *
 ********************************************************************************************/

using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Xml.Linq;
using System.Text.RegularExpressions;
namespace BL
{
    public class TXTProgramOperations_Safety
    {
        public static List<program> TemplateFile;
        // const int MAXNETWORKCOUNT = 2;
         int MAXNETWORKCOUNT = XMLOperations_LadderLogic.TemplateSelction=="B"? 3 : 2;
         int MAXNETWORKCOUNTST = XMLOperations_LadderLogic.TemplateSelction == "B" ? 4 :  5;
        int MAXCONTACTSCOUNT = XMLOperations_LadderLogic.TemplateSelction == "B" ? 8 : 10;
        public static XElement TemplateXMLFile { get; internal set; }
        public static bool SpacerUse=false;
        public static bool STConstantProgram;
        public static List<string> mySTprogram;
        public static List<string> mySTConfiguration;


        //serlize outputvariable

        public static outputVariables DeserializeSafetyoutputVariables(XElement thisoupvar, string fatherbBLkID)
        {
            outputVariables newouppin = new outputVariables();

            var varO = from O in thisoupvar.Descendants()
                       where O.Name.LocalName == "variable"
                       select O;

            foreach (XElement thispvarO in varO)
            {
                //ariable newvarO = new variable();

                //here change

                variable newOvariable = new variable();
                newOvariable.fatherblockID = fatherbBLkID;

                newOvariable.formalParameter = thispvarO.Attribute("formalParameter").Value;
                //newOvariable.fatherblockID = newBlk.localId;
                newOvariable.Visible = variable.VISIBILITY_RIGHT;

                // if (thispvar.ToString().Contains("refLocalId"))
                //{
                var connectionPointOutlist = (from P in thispvarO.Descendants()
                                              where P.Name.LocalName == "connectionPointOut"
                                              select P).ToList();
                if (connectionPointOutlist.Count > 0)
                //foreach (XElement thispconnectionPointOut in connectionPointOutlist)
                {
                    XElement thispconnectionPointOut = connectionPointOutlist[0];

                    connectionPointOut newconnectionPointOut = new connectionPointOut();
                    var relPositionlist = from Q in thispconnectionPointOut.Descendants()
                                          where Q.Name.LocalName == "relPosition"
                                          select Q;
                    foreach (XElement thisrelPosition in relPositionlist)
                    {
                        relPosition newrelPosition = new relPosition();
                        newrelPosition.X = thisrelPosition.Attribute("x").Value;
                        newrelPosition.Y = thisrelPosition.Attribute("y").Value;
                        newconnectionPointOut.relPositionMap.Add(newrelPosition);
                    }

                    newOvariable.connectionPointOutMap = newconnectionPointOut;
                }

                newouppin.variableMap.Add(newOvariable.formalParameter, newOvariable);
            }
            return newouppin;
        }

        //serlize inputvariable
        public static inputVariables DeserializeSafetyinputVariables(XElement thisinpvar, string fatherbBLkID)
        {
            inputVariables newinppin = new inputVariables();

            var varI = from f in thisinpvar.Descendants()
                       where f.Name.LocalName == "variable"
                       select f;

            foreach (XElement thispvar in varI)
            {
                variable newvar = new variable();
                newvar.formalParameter = thispvar.Attribute("formalParameter").Value;
                newvar.fatherblockID = fatherbBLkID;
                newvar.Visible = variable.VISIBILITY_LEFT;

                if (thispvar.ToString().Contains("refLocalId"))
                {
                    var connectionPointInlist = (from g in thispvar.Descendants()
                                                 where g.Name.LocalName == "connectionPointIn"
                                                 select g).ToList();
                    if (connectionPointInlist.Count > 0)

                    //foreach (XElement thispconnectionPointIn in connectionPointInlist)
                    {
                        XElement thispconnectionPointIn = connectionPointInlist[0];

                        connectionPointIn newconnectionPointIn = new connectionPointIn();
                        var relPositionlist = from k in thispconnectionPointIn.Descendants()
                                              where k.Name.LocalName == "relPosition"
                                              select k;
                        foreach (XElement thisrelPosition in relPositionlist)
                        {
                            relPosition newrelPosition = new relPosition();
                            newrelPosition.X = thisrelPosition.Attribute("x").Value;
                            newrelPosition.Y = thisrelPosition.Attribute("y").Value;
                            newconnectionPointIn.relPositionMap.Add(newrelPosition);
                            var connectionlist = (from L in thispconnectionPointIn.Descendants()
                                                  where L.Name.LocalName == "connection"
                                                  select L).ToList();
                            if (connectionlist.Count > 0)
                            // foreach (XElement thisconnection in connectionlist)
                            {
                                XElement thisconnection = connectionlist[0];

                                connection newconnection = new connection();
                                newconnection.refLocalId = thisconnection.Attribute("refLocalId").Value;
                                newconnection.MyXML = thisconnection;
                                if (thisconnection.ToString().Contains("formalParameter"))
                                {
                                    newconnection.formalParameter = thisconnection.Attribute("formalParameter").Value;
                                }

                                var positionlist = from M in thisconnection.Descendants()
                                                   where M.Name.LocalName == "position"
                                                   select M;
                                foreach (XElement thisposition in positionlist)
                                {
                                    position newposition = new position();
                                    newposition.X = thisposition.Attribute("x").Value;
                                    newposition.Y = thisposition.Attribute("y").Value;
                                    // newconnection.positionMap.Add(newposition.X + '_' + newposition.Y ,newposition);
                                    newconnection.positionMap.Add(newposition);
                                }
                                newconnectionPointIn.connectionMap = newconnection;
                            }
                        }
                        newvar.connectionPointInMap = newconnectionPointIn;//need to add key
                    }
                }

                newinppin.variableMap.Add(newvar.formalParameter, newvar);//need to add key
            }

            return newinppin;
        }

        //serlize inoutvariable

        public static inOutVariable DeserializeSafetyinOutVariable(XElement thisIO)
        {
            inOutVariable newinOutVariable = new inOutVariable();

            newinOutVariable.localId = thisIO.Attribute("localId").Value;
            newinOutVariable.MyXML = thisIO;
            newinOutVariable.width = thisIO.Attribute("width").Value;
            newinOutVariable.height = thisIO.Attribute("height").Value;
            if (thisIO.Name.LocalName == "inVariable")
            {
                newinOutVariable.VariableType = "Constant";
                newinOutVariable.TagAttributesMap.VariablType = "Constant";
                XElement DType = thisIO.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "DATATYPE");
                if (DType != null)
                {
                    newinOutVariable.TagAttributesMap.DATATYPE = DType.Value;
                }
                else
                {//should be revised
                    newinOutVariable.TagAttributesMap.DATATYPE = "";
                }
            }
            if (thisIO.Name.LocalName == "inOutVariable")
            {
                newinOutVariable.VariableType = "Global_Variable or Local_Varaible";
            }

            var postionlist = from S in thisIO.Descendants()
                              where S.Name.LocalName == "position" && S.ToString().Contains("ts:SheetNumber")
                              select S;
            XElement thispostion = postionlist.First();

            if (thispostion != null)
            {
                newinOutVariable.positionMap.X = thispostion.Attribute("x").Value;

                newinOutVariable.positionMap.Y = thispostion.Attribute("y").Value;
                //string sheetattapute = "ts" + ':' + "SheetNumber";
                // newinOutVariable.positionMap.SheetNumber = thispostion.LastAttribute.Value;//ToDo:there is problem to get attribute contain :

                var nameAttributes =
                                     from attr in thispostion.Attributes()
                                     where attr.Name.ToString().Contains("SheetNumber")
                                     select attr;
                foreach (var attribute in nameAttributes)
                {
                    newinOutVariable.positionMap.SheetNumber = attribute.Value;
                }
            }

            var connectionlist = from L in thisIO.Descendants()
                                 where L.Name.LocalName == "connection"
                                 select L;

            if (connectionlist != null)
            {
                foreach (XElement thisconnection in connectionlist)
                {
                    connection newconnection = new connection();
                    newconnection.refLocalId = thisconnection.Attribute("refLocalId").Value;
                    newconnection.MyXML = thisconnection;

                    if (thisconnection.ToString().Contains("formalParameter"))
                    {
                        newconnection.formalParameter = thisconnection.Attribute("formalParameter").Value;
                    }

                    var positionlist = from M in thisconnection.Descendants()
                                       where M.Name.LocalName == "position"
                                       select M;
                    foreach (XElement thisposition in positionlist)
                    {
                        position newposition = new position();
                        newposition.X = thisposition.Attribute("x").Value;
                        newposition.Y = thisposition.Attribute("y").Value;
                        // newconnection.positionMap.Add(newposition.X + '_' + newposition.Y, newposition);//need id
                        newconnection.positionMap.Add(newposition);
                    }

                    newinOutVariable.connectionMap = newconnection;
                }
            }
            var expressionlist = (from N in thisIO.Descendants()
                                  where N.Name.LocalName == "expression"
                                  select N).ToList();
            if (expressionlist.Count > 0)
            //foreach (XElement thisexpression in expressionlist)
            {
                XElement thisexpression = expressionlist[0];
                expression newexpression = new expression();
                newexpression.Tagname = thisexpression.Value;

                newinOutVariable.TagAttributesMap.Tagname = thisexpression.Value;
                newinOutVariable.expressionMap = newexpression;//need id
            }

            return newinOutVariable;
        }

        //serlize block
        public static SafetyBlock DeserializeSafetyBlock(XElement thisBlk)
        {
            SafetyBlock newBlk = new SafetyBlock();

            newBlk.localId = thisBlk.Attribute("localId").Value;
            newBlk.width = thisBlk.Attribute("width").Value;
            newBlk.height = thisBlk.Attribute("height").Value;
            newBlk.MyXML = thisBlk;

            if (thisBlk.ToString().Contains("instanceName"))
            {
                newBlk.instanceName = thisBlk.Attribute("instanceName").Value;
                newBlk.BlockAttributes.BlockName = thisBlk.Attribute("instanceName").Value;
            }

            newBlk.typeName = thisBlk.Attribute("typeName").Value;
            newBlk.BlockAttributes.BlockType = thisBlk.Attribute("typeName").Value;

            var posList = (from x in thisBlk.Descendants()
                           where x.Name.LocalName == "position"
                           select x).ToList();

            XElement thisPosition = posList[0];

            if (thisPosition.ToString().Contains("SheetNumber"))
            {
                //position newPosition = new position();
                newBlk.positionMap.X = thisPosition.Attribute("x").Value;
                newBlk.positionMap.Y = thisPosition.Attribute("y").Value;
                //string sheetattapute = "ts" + ':' + "SheetNumber";
                var nameAttributes =
                                 from attr in thisPosition.Attributes()
                                 where attr.Name.ToString().Contains("SheetNumber")
                                 select attr;
                foreach (var attribute in nameAttributes)
                {
                    newBlk.positionMap.SheetNumber = attribute.Value;
                }
                //  newBlk.positionMap.SheetNumber = thisPosition.LastAttribute.Value;//there is problem to get attribute contain :
                //newBlk.positionMap.SheetNumber = thisPosition.Attribute("ts:SheetNumber").Value;
            }

            var inpList = (from e in thisBlk.Descendants()
                           where e.Name.LocalName == "inputVariables"
                           select e).ToList();

            //foreach (XElement thisinpvar in inpList)

            if (inpList.Count > 0)
            {
                XElement thisinpvar = inpList[0];

                inputVariables newinppin = DeserializeSafetyinputVariables(thisinpvar, newBlk.localId);

                newBlk.inputVariablesMap = newinppin;
            }

            //beginoutput
            var oupList = (from N in thisBlk.Descendants()
                           where N.Name.LocalName == "outputVariables"
                           select N).ToList();

            if (oupList.Count > 0)

            //foreach (XElement thisoupvar in oupList)
            {
                XElement thisoupvar = oupList[0];

                outputVariables newouppin = DeserializeSafetyoutputVariables(thisoupvar, newBlk.localId);

                newBlk.outputVariablesMap = newouppin;
            }

            return newBlk;
        }

        //serlize inoutvariable

        public Dictionary<string, SafetyTemplate> DeserializeSafetyTemplate(string[] ioFlgList, string ProgramName, ref List<string> mylogic, ref SortedDictionary<int, int> IDS, ref List<string> Library, DataTable myDB)
        {
            Dictionary<string, SafetyTemplate> newSafetyTemplateMap = new Dictionary<string, SafetyTemplate>();
            List<coil> mycoillist = new List<coil>();

            var CompoundList = ioFlgList.Select((Value, Index) => new { Value, Index })
                 .Where(p => p.Value.Contains("Page")).ToList();
            if ((CompoundList != null) && (CompoundList.Count() != 0))
            {
                int thisCompoundindex = 0;
                foreach (var thisCompound in CompoundList)
                {
                    int upperIndex;
                    if (thisCompoundindex + 1 < CompoundList.Count())
                    {
                        upperIndex = CompoundList[thisCompoundindex + 1].Index;
                    }
                    else
                    {
                        upperIndex = ioFlgList.Count();
                    }

                    int lowerIndex = thisCompound.Index;
                    //Compound newCompound = new Compound();

                    thisCompoundindex++;

                    try
                    {
                        DeserializeSheet(ioFlgList.ToList().GetRange(lowerIndex, upperIndex - lowerIndex), ref newSafetyTemplateMap, ref mycoillist, ProgramName, ref mylogic, ref IDS, ref Library, myDB);
                    }
                    catch (Exception ex)
                    {
                        string tt = ex.Message;
                    }
                }
            }

            return newSafetyTemplateMap;
        }

        private static void CreateFBDSafetyTemplate(ref SafetyTemplate mynewSafetyTemplate)
        {
            // throw new NotImplementedException();
            mynewSafetyTemplate = new SafetyTemplate(XMLOperations_LadderLogic.BlankProgram.FirstOrDefault(pr=>pr.Name.Contains("TEMPLATES")).SafetyTemplateMap.FirstOrDefault(sh => sh.Value.SheetNumber == "2").Value);
            string tt = mynewSafetyTemplate.ProgramName;
            //  XMLOperations_safety.ModifySafetTemplatePosition(ref myTemplate, mynewSafetyTemplate.SheetNumber);
        }

        private static void CreatePackSafetyTemplate(ref SafetyTemplate mynewSafetyTemplate, ref SortedDictionary<int, int> IDS, string packcommand)
        {
            // throw new NotImplementedException();
            XMLManager tt = new XMLManager();
            if (packcommand.Contains("UNPACK"))
            {
                mynewSafetyTemplate = tt.DuplicateSafetyTemplate(new SafetyTemplate(XMLOperations_LadderLogic.BlankProgram.FirstOrDefault(pr => pr.Name.Contains("TEMPLATES")).SafetyTemplateMap.FirstOrDefault(sh => sh.Value.SheetNumber == "3").Value), IDS);
            }
            else
            {
                mynewSafetyTemplate = tt.DuplicateSafetyTemplate(new SafetyTemplate(XMLOperations_LadderLogic.BlankProgram.FirstOrDefault(pr => pr.Name.Contains("TEMPLATES")).SafetyTemplateMap.FirstOrDefault(sh => sh.Value.SheetNumber == "4").Value), IDS);
            }

            //  XMLOperations_safety.ModifySafetTemplatePosition(ref myTemplate, mynewSafetyTemplate.SheetNumber);
        }

        private void DeserializeSheet(List<string> list, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref List<coil> mycoillist, string programName, ref List<string> mylogic, ref SortedDictionary<int, int> IDS, ref List<string> Library, DataTable myDB)
        {
            string pagenumber = list[0].Substring(list[0].IndexOf("Page") + 5, list[0].Length - (list[0].IndexOf("Page") + 5)).Trim();
            string networks = "";
            // SafetyTemplate mynewSafetyTemplate = null;

            string sheettitle = "";
            // SafetyTemplate mynewSafetyTemplate = null;
            //
            SafetyTemplate mynewSafetyTemplate = newSafetyTemplateMap.Values.LastOrDefault();
            int safetytemplatescount = newSafetyTemplateMap.Count() + 2;
            if (mynewSafetyTemplate == null)
            {
                CreateFBDSafetyTemplate(ref mynewSafetyTemplate);

                mynewSafetyTemplate.SheetNumber = safetytemplatescount.ToString();

                mynewSafetyTemplate.ProgramName = programName;
                Duplicatecomments(ref mynewSafetyTemplate, ref IDS);
                XMLOperations_safety.ModifySafetTemplatePosition(ref mynewSafetyTemplate, mynewSafetyTemplate.SheetNumber);
                sheettitle = "Page:" + pagenumber + " Network #";
            }
            else
            {
                sheettitle = mynewSafetyTemplate.Name + " " + "Page:" + pagenumber + " Network #";
            }
            //
         

            // int safetytemplatescount = newSafetyTemplateMap.Count() + 5;

            int networkcountinsheet = 0;

            //if (mynewSafetyTemplate.SheetNumber != null)
            //{
            //    newSafetyTemplateMap.Add(mynewSafetyTemplate.SheetNumber, mynewSafetyTemplate);
            //}

            var CompoundList = list.Select((Value, Index) => new { Value, Index })
                .Where(p => p.Value.Contains("Network #")).ToList();
            if ((CompoundList != null) && (CompoundList.Count() != 0))
            {
                int thisCompoundindex = 0;
                foreach (var thisCompound in CompoundList)
                {
                    string networkline = thisCompound.Value.Replace(" ", "");
                    int k = networkline.IndexOf("Network#") + 8;
                    int y = networkline.IndexOf(":");

                    string first = "";
                    string last = "";

                    string networkenumber = networkline.Substring(k, y - k).Trim();
                    if (networks != "")
                    {
                        networks = networks + "," + networkenumber;
                    }
                    else
                    {
                        networks = sheettitle.EndsWith("#") ? sheettitle + networkenumber : sheettitle + "," + networkenumber;
                      
                    }
                    
                    sheettitle = networks;
                    if(sheettitle.Length>=45)
                    {
                        string[] values = sheettitle.Split(',');
                        first = values[0];
                        last = values.LastOrDefault();

                        if(sheettitle.Contains("To") && !sheettitle.Contains(","))
                        {
                            
                            values = Regex.Split(sheettitle, "To"); ;

                            first = values[0];
                            last = values.LastOrDefault();

                        }

                        sheettitle =first+" To " +last;
                    }
                    int upperIndex;
                    if (thisCompoundindex + 1 < CompoundList.Count())
                    {
                        upperIndex = CompoundList[thisCompoundindex + 1].Index;
                    }
                    else
                    {
                        upperIndex = list.Count();
                    }

                    int lowerIndex = thisCompound.Index;
                    //Compound newCompound = new Compound();

                    thisCompoundindex++;
                    List<string> ComundList, TempList;
                    PrepareTheBasicSafetySheet(list, newSafetyTemplateMap, programName, ref IDS, pagenumber, ref networks, ref sheettitle, ref mynewSafetyTemplate, ref safetytemplatescount, networkenumber, upperIndex, lowerIndex, out ComundList, out TempList, mylogic,"Page:");
                    if(TempList.Count()==0)
                    {
                        updatesheettitles(sheettitle, ref mynewSafetyTemplate);
                    }
                    else
                    {
                        updatesheettitles(sheettitle, ref mynewSafetyTemplate);
                    }
                    
                    DeserializeNetwork(list.ToList().GetRange(lowerIndex, upperIndex - lowerIndex), ref mynewSafetyTemplate, ref newSafetyTemplateMap, ref mycoillist, ref mylogic, ref IDS, ref Library, myDB, networkenumber);

                    if (mynewSafetyTemplate.SheetNumber != null)
                    {
                        var sheet = newSafetyTemplateMap.FirstOrDefault(ke => ke.Key == mynewSafetyTemplate.SheetNumber).Value;
                        if (sheet == null)
                        {
                            newSafetyTemplateMap.Add(mynewSafetyTemplate.SheetNumber, mynewSafetyTemplate);
                        }
                    }
                    if (ComundList.Count() != 0 || TempList.Count() != 0)
                    {
                        networks = "";
                    }
                    else
                    {
                        if (!mynewSafetyTemplate.Name.Contains("PACK"))
                        {
                            networkcountinsheet++;
                        }
                    }
                }
            }
        }

        private void PrepareTheBasicSafetySheet(List<string> list, Dictionary<string, SafetyTemplate> newSafetyTemplateMap, string programName, ref SortedDictionary<int, int> IDS, string pagenumber, ref string networks, ref string sheettitle, ref SafetyTemplate mynewSafetyTemplate, ref int safetytemplatescount, string networkenumber, int upperIndex, int lowerIndex, out List<string> ComundList, out List<string> TempList, List<string> mylogic,string reference)
        {
            var range = upperIndex - lowerIndex <= 0 ? 1 : upperIndex - lowerIndex;
            List<string> mynetwork = list.ToList().GetRange(lowerIndex, range);
            ComundList = mynetwork.Where
       (p => p.Contains("PACK") && p.Contains("=") && p != "" && p.Contains("(")).ToList();
            TempList = mynetwork.Where
        (p => p.ToUpper().Contains("TEMPLATE") && p.Contains("=") && p != "" && p.Contains("(")).ToList();
            //add new here
            List<string> NetworkDiscption = SerializeDescription(mynetwork);
            //

            //if (networkcountinsheet % 3 == 0 || (mynewSafetyTemplate != null && (mynewSafetyTemplate.Networkcount > 3)) || (ComundList.Count() != 0) || mynewSafetyTemplate.SafetycontactMap.Count() > 10 || (mynewSafetyTemplate.Name != null && mynewSafetyTemplate.Name.Contains("PACK")) || mynewSafetyTemplate == null)
            if ((mynewSafetyTemplate != null && (mynewSafetyTemplate.Networkcount > MAXNETWORKCOUNT && !SpacerUse)) || CheckNetworkDescription(NetworkDiscption) || TempList.Count() != 0 || (ComundList.Count() != 0) ||( mynewSafetyTemplate.SafetycontactMap.Count() > MAXCONTACTSCOUNT && !SpacerUse|| mynewSafetyTemplate.SafetycontactMap.Count() >= MAXCONTACTSCOUNT) || (mynewSafetyTemplate.Name != null && mynewSafetyTemplate.Name.Contains("PACK")) ||
                (CheckTemplate(mynewSafetyTemplate)) || mynewSafetyTemplate == null)
            //if ( CheckNetworkDescription(NetworkDiscption) || TempList.Count() != 0 || (ComundList.Count() != 0) || mynewSafetyTemplate.SafetycontactMap.Count() > 10 || (mynewSafetyTemplate.Name != null && mynewSafetyTemplate.Name.Contains("PACK")) ||
            //    (CheckTemplate(mynewSafetyTemplate)) || mynewSafetyTemplate == null)
            {
                networks = "";
                sheettitle = reference + pagenumber + " Network #" + networkenumber;
                mynewSafetyTemplate = new SafetyTemplate();
                if ((ComundList.Count() != 0))
                {
                    int equalindex = ComundList.FirstOrDefault().ToUpper().IndexOf("=");
                    int practisindex = ComundList.FirstOrDefault().ToUpper().IndexOf("(");
                    string command = ComundList.FirstOrDefault().Substring(equalindex + 1, (practisindex - equalindex) - 1).Trim();
                    CreatePackSafetyTemplate(ref mynewSafetyTemplate, ref IDS, command);
                    //thisCompoundindex--;
                }
                else
                {

                    if (TempList.Count() == 0)
                    {
                        CreateFBDSafetyTemplate(ref mynewSafetyTemplate);
                        
                    }
                    else
                    {
                        
                        mynewSafetyTemplate = CreateFromTemplate(IDS, list.ToList().GetRange(lowerIndex, upperIndex - lowerIndex), mylogic, networkenumber,ref sheettitle);
                        if(mynewSafetyTemplate==null|| mynewSafetyTemplate.Template=="")
                        {

                        }
                       // PrepareNamesForTemplate(ref mynewSafetyTemplate, "TEMPLATE");
                    }


                }

                safetytemplatescount = Addnewsheettemplate(newSafetyTemplateMap, programName, ref IDS, ref mynewSafetyTemplate);

                //if (mynewSafetyTemplate.SheetNumber != null)
                //{
                //    newSafetyTemplateMap.Add(mynewSafeAddnewsheettemplatetyTemplate.SheetNumber, mynewSafetyTemplate);
                //}
            }
        }

        private bool CheckNetworkDescription(List<string> networkDiscption)
        {
            bool returnValue = false;
            foreach (var sentence in networkDiscption)
            {
                if(sentence.Contains("(SPACER)"))
                {
                    returnValue = true;
                    break;
                }
            }
            return returnValue;
        }

        private  SafetyTemplate CreateFromTemplate(SortedDictionary<int, int> IDS,List<string> list, List<string> mylogic, string networkenumber,ref string sheettitle)
        {

            SafetyTemplate mynewSafetyTemplate;
            XMLManager tt = new XMLManager();
            if (TemplateFile == null)
            {
                string file = OpenXMLFile("open template file");
                TemplateXMLFile = FileWorks.LoadXMLFile(file);
                TemplateFile = XMLOperations_safety.DeserializeSafetyProgram(TemplateXMLFile);

            }
            string templatename, command;
            GetTemplateName(list, out templatename, out command);
            sheettitle = templatename + "/" + sheettitle;
            mynewSafetyTemplate = tt.DuplicateSafetyTemplate(new SafetyTemplate(SearchAboutTemplate(templatename, mylogic, networkenumber)), IDS);
            return mynewSafetyTemplate;
        }

        private SafetyTemplate SearchAboutTemplate(string name, List<string> mylogic, string networkenumber)
        {

            SafetyTemplate newtemp = null;
            foreach(var prg in TemplateFile)
            {
                newtemp = prg.SafetyTemplateMap.FirstOrDefault(x => x.Value.Template == name).Value;
                if(newtemp!=null)
                {
                    break;
                }
            }
            if (newtemp==null)
            {
                mylogic.Add("Network # "+ networkenumber+ ": can't be converted: " +name +" Is missed in template File ,LPT contain template not provided");
                 CreateFBDSafetyTemplate(ref newtemp);
            }

            return newtemp;
        }

        public string OpenXMLFile(string title)
        {
            // The headless host never prompts for a file; callers pass paths explicitly.
            return null;
        }
        private bool CheckAndUpdateTemplateSafetyTemplate(List<string> list, List<string> networkDiscption, ref SafetyTemplate mynewSafetyTemplate, Dictionary<string, SafetyTemplate> SafetyTemplateMap,string networknumber, ref SortedDictionary<int, int> iDS)
        {
            bool returnvalue = CheckTemplate(list);
            
            string templatename, command;
          
            if (returnvalue)
            {
                GetTemplateName(list, out templatename, out command);
                if(networknumber=="13"|| networknumber == "15")
                {

                }
                Comment mycomment = FindTemplateComment(mynewSafetyTemplate, "Network #");
                if (IsTemplateExist(SafetyTemplateMap, templatename + command,ref mynewSafetyTemplate))
                {

                    mycomment = FindTemplateComment(mynewSafetyTemplate, mynewSafetyTemplate.NetworkDescription.FirstOrDefault());
                }
                else
                {
                   
                    mynewSafetyTemplate.NetworkDescription = new List<string>();
                }
                UpdateTemplateSafetyTemplate(networknumber, networkDiscption, ref mynewSafetyTemplate);
                ModifyCommentFromListString(mynewSafetyTemplate.NetworkDescription, ref mycomment);
                PrepareNamesForTemplate(ref mynewSafetyTemplate, templatename+command);
            }
            return returnvalue;
        }

        private Comment FindTemplateComment(SafetyTemplate mynewSafetyTemplate,string networkind)
        {
            var Comments = mynewSafetyTemplate.CommentMap.Values.Where(x=>x.content.Contains(networkind)).ToList();
            Comment comment = Comments.FirstOrDefault();

            return comment;
        }

        private static void ModifyCommentFromListString(List<string> networkDescription, ref Comment mycomment)
        {
            if (mycomment!=null)
            {
                string mystring = GetonestringfromList(networkDescription);
                //int commenthieght = (networkDescription.Count() * 4);
                //XMLOperations_safety.ModifyCommentHieght(ref mycomment, commenthieght);
                XMLOperations_safety.ModifyCommentContent(ref mycomment, mystring);
            }
           
            
        }
        private static string GetonestringfromList(List<string> mylist)
        {
            string mystring = "";
            foreach (var line in mylist)
            {
                mystring = mystring + line + Convert.ToChar(13);
            }

            if (mylist.Count() > 1)
            {
            }
            return mystring;
        }
        private bool IsTemplateExist(Dictionary<string, SafetyTemplate> safetyTemplateMap, string templatename,ref SafetyTemplate mynewSafetyTemplate)
        {
            bool returnvalue = false;
            var existed = safetyTemplateMap.FirstOrDefault(x => x.Value.Template == templatename).Value;
            if(existed!=null)
            {
                mynewSafetyTemplate = existed;
                returnvalue = true;
            }
            return returnvalue;
        }

        private void UpdateTemplateSafetyTemplate(string networknumber, List<string> networkDiscption, ref SafetyTemplate mynewSafetyTemplate)
        {
            mynewSafetyTemplate.NetworkDescription.Add("Network #"+ networknumber);
            foreach (var descr in networkDiscption)
            {
                mynewSafetyTemplate.NetworkDescription.Add(descr);
            }
            
        }

        private  bool CheckTemplate(List<string> list)
        {
            bool returnvalue = false;
            
            var tempList = list
            .Where(p => p.ToUpper().Contains("TEMPLATE") && p.Contains("=") && p != "" && p.Contains("(")).ToList();
            if (returnvalue = tempList.Count() != 0)
            {
                
            }

            return returnvalue;
        }

        private void PrepareNamesForTemplate(ref SafetyTemplate mynewSafetyTemplate, string command)
        {
            mynewSafetyTemplate.Name = mynewSafetyTemplate.Name + command;
            mynewSafetyTemplate.Template =  command;
           
        }

        private static void GetTemplateName(List<string> tempList, out string templatename, out string command)
        {
            var list = tempList
.Where(p => p.ToUpper().Contains("TEMPLATE") && p.Contains("=") && p != "" && p.Contains("(")).ToList();
            int equalindex = list.FirstOrDefault().ToUpper().IndexOf("=");
            int practisindex = list.FirstOrDefault().ToUpper().IndexOf("(");
            templatename = list.FirstOrDefault().Substring(0, equalindex).Trim().Replace("-|", "").Replace("|", "").Replace(" ", "").Replace("-", "_").Replace("+", "").Replace(";", "");
            command = list.FirstOrDefault().Substring(equalindex + 1, (practisindex - equalindex) - 1).Trim();
            command = command.Trim().Replace("-|", "").Replace("|", "").Replace(" ", "").Replace("-", "_").Replace("+", "").Replace(";", "");
        }

        private bool CheckTemplate(SafetyTemplate mynewSafetyTemplate)
        {
            bool returnvalue = false;
            if (mynewSafetyTemplate != null )
            {
                returnvalue = mynewSafetyTemplate.Template.Contains("TEMPLATE");
            }
            if (returnvalue)
            {

            }
            return returnvalue;
        }
        private  void updatesheettitles(string sheettitle, ref SafetyTemplate mynewSafetyTemplate)
        {
          
            Modifycommentnetwork(ref mynewSafetyTemplate, sheettitle);
            
            XMLOperations_safety.ModifySafetyTemplateTitle(ref mynewSafetyTemplate, sheettitle);
         
           
        }

        private static int Addnewsheettemplate(Dictionary<string, SafetyTemplate> newSafetyTemplateMap, string programName, ref SortedDictionary<int, int> IDS, ref SafetyTemplate mynewSafetyTemplate)
        {
            // int safetytemplatescount = newSafetyTemplateMap.Count() + 5;
            int safetytemplatescount = newSafetyTemplateMap.Count() + 2;
            mynewSafetyTemplate.SheetNumber = safetytemplatescount.ToString();

            mynewSafetyTemplate.ProgramName = programName;
            Duplicatecomments(ref mynewSafetyTemplate, ref IDS);
            XMLOperations_safety.ModifySafetTemplatePosition(ref mynewSafetyTemplate, mynewSafetyTemplate.SheetNumber);
            return safetytemplatescount;
        }

        private static void Modifycommentnetwork(ref SafetyTemplate mynewSafetyTemplate, string sheettitle)
        {
            Comment mycomment = mynewSafetyTemplate.CommentMap.FirstOrDefault(com => com.Value.content.ToUpper().Replace(" ","").Contains("NETWORK#")).Value;
            if (mycomment != null)
            {
                var textsize = (from c in mycomment.MyXML.Descendants()
                                where c.Name.LocalName == "TEXTSIZE"
                                select c).FirstOrDefault();
                textsize.Value = XMLOperations_LadderLogic.CommentTextSize;
                XMLOperations_safety.ModifyCommentContent(ref mycomment, sheettitle);


            }
            else
            {
            }
        }

        private static void Duplicatecomments(ref SafetyTemplate modSafetytemplate, ref SortedDictionary<int, int> iDS)
        {
            foreach (Comment commnt in modSafetytemplate.CommentMap.Values)
            {
                string oldID = commnt.localId;

                string newID = (iDS.ElementAt(iDS.Count() - 1).Value + 1).ToString();
                iDS.Add(int.Parse(newID), int.Parse(newID));

                SearchIDinComment(ref modSafetytemplate, oldID, newID);
            }
        }

        private static void SearchIDinComment(ref SafetyTemplate modSafetyTemplate, string oldID, string newTagID)
        {
            XElement commentXml = null;

            var commentvalue = modSafetyTemplate.CommentMap.FirstOrDefault(cmnt => cmnt.Value.localId == oldID).Value;
            Comment mycomment;
            if (commentvalue != null)
            {
                commentXml = commentvalue.MyXML;
                mycomment = modSafetyTemplate.CommentMap.FirstOrDefault(cmnt => cmnt.Value.localId == oldID).Value;
            }

            if (commentXml != null)
            {
                commentXml.Attribute("localId").Value = newTagID;

                commentvalue.localId = newTagID;
            }
        }

        private static void Duplicatecomments(ref SafetyTemplate mynewSafetyTemplate)
        {
            throw new NotImplementedException();
        }

        private void DeserializeNetwork(List<string> list, ref SafetyTemplate mynewSafetyTemplate, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref List<coil> mycoillist, ref List<string> mylogic, ref SortedDictionary<int, int> IDS, ref List<string> Library, DataTable myDB, string networknumber)
        {
            try
            {
                string Logic = "";

                int begin = 0;
                List<contact> mycontactlist = new List<contact>();
                List<string> networkDiscption = SerializeDescription(list);
                if(list.FirstOrDefault().Contains("PROGRAM"))
                {
                    GetnewProgramName(ref mynewSafetyTemplate, list.FirstOrDefault());
                }

                if (!SerializePack(list, ref Logic, ref begin, 1000, ref mynewSafetyTemplate, ref IDS, ref mycontactlist) && !CheckIfmixedSTandLadder(list, networknumber, mylogic) &&  
             
                        !CheckAndUpdateTemplateSafetyTemplate(list, networkDiscption, ref mynewSafetyTemplate, newSafetyTemplateMap, networknumber, ref IDS)
                   )
                {
                    networkDiscption = ModifyCommentNetworkFromSpacer(mynewSafetyTemplate, networkDiscption, networknumber);
                    SerializeST(list, ref mynewSafetyTemplate, ref newSafetyTemplateMap, networkDiscption, ref IDS, ref Library, myDB, networknumber, mylogic);
                    SerializeLogicMod(list, ref Logic, ref begin, 1000, ref mynewSafetyTemplate, ref IDS, ref mycontactlist, ref mycoillist, ref Library, myDB, networkDiscption, ref newSafetyTemplateMap, networknumber);

                    calculateDescrptionSize(mynewSafetyTemplate, networkDiscption);
                    calculateParrallelSize(mynewSafetyTemplate);
                    mynewSafetyTemplate.Networkcount++;

                    ADDLatchingcoils(ref Logic, ref mynewSafetyTemplate, ref IDS, ref mycontactlist, ref mycoillist, myDB, networkDiscption, ref newSafetyTemplateMap, networknumber);

                    Checkbugexist(ref mynewSafetyTemplate, Logic);
                }

                mylogic.Add(Logic);
            }
            catch (Exception EX)
            {
                mylogic.Add("Network # " + networknumber + ": can't be converted:this network need manual work");
                string TT = EX.Message;
            }
        }

        private void GetnewProgramName(ref SafetyTemplate mynewSafetyTemplate, string networkheader)
        {
           string programname= networkheader.Split('=')[1];
            mynewSafetyTemplate.CreateNewProgram = programname.Trim();
        }

        private List<string> ModifyCommentNetworkFromSpacer(SafetyTemplate mynewSafetyTemplate, List<string> networkDiscption, string networknumber)
        {
            Comment mycomment = FindTemplateComment(mynewSafetyTemplate, "Network #");
            if(mynewSafetyTemplate.NetworkDescription==null)
            {
                mynewSafetyTemplate.NetworkDescription = new List<string>();
            }
            if ((CheckNetworkDescription(networkDiscption)|| mynewSafetyTemplate.Networkcount==0) && mycomment != null)
            {
                mynewSafetyTemplate.NetworkDescription.Add("Network #" + networknumber);
                foreach (var descr in networkDiscption)
                {
                    mynewSafetyTemplate.NetworkDescription.Add(descr);
                }
                
                ModifyCommentFromListString(mynewSafetyTemplate.NetworkDescription, ref mycomment);
                networkDiscption = new List<string>();

            }
            else
            {
                if(mycomment != null )
                {
                    if(mynewSafetyTemplate.NetworkDescription.Any())
                    {
                        mynewSafetyTemplate.NetworkDescription[0] = mynewSafetyTemplate.NetworkDescription[0] + "," + networknumber;
                    }
                    else
                    {
                        mynewSafetyTemplate.NetworkDescription.Add("Network #" + networknumber);
                    }
                    ModifyCommentFromListString(mynewSafetyTemplate.NetworkDescription, ref mycomment);

                }
            }

            return networkDiscption;
        }

        private void ADDLatchingcoils(ref string logic, ref SafetyTemplate mynewSafetyTemplate, ref SortedDictionary<int, int> iDS, ref List<contact> mycontactlist, ref List<coil> mycoillist, DataTable myDB, List<string> networkDiscption, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, string networknumber)
        {
            var coilnetworks = mynewSafetyTemplate.SafetycoilMap.Values.Where(x => x.Networknumber == networknumber && (x.Name.Contains("_LCH") || x.Name.Contains("_TMR") || x.Name.Contains("_RST") || x.Name.Contains("_CTU"))).ToList();
            if (coilnetworks.Count() != 0)
            {
                foreach (coil mycoil in coilnetworks)
                {
                    if (mynewSafetyTemplate.Networkcount > MAXNETWORKCOUNT)
                    {
                        AddnewTemplate(ref mynewSafetyTemplate, newSafetyTemplateMap, ref iDS);
                    }

                    mycoillist.Add(mycoil);
                    AdjustLocgicForLCH(mycoil.Name, ref mynewSafetyTemplate, ref newSafetyTemplateMap, ref iDS, mycoillist, mycoil.timer, mycoil.Coiltype, networkDiscption, networknumber);
                }
            }
        }

        private static bool CheckIfmixedSTandLadder(List<string> list, string networknumber, List<string> mylogic)
        {
            bool returnvalue = false;
            var STList = list.Select((Value, Index) => new { Value, Index })
                .Where(p => p.Value.Contains("+--") && !p.Value.Contains("]") && p.Value.Contains("--+") && p.Value.IndexOf("--+") > p.Value.IndexOf("+--")).ToList();
            var LadderList = list.Select((Value, Index) => new { Value, Index })
               .Where(p => p.Value.Contains("]") || p.Value.Contains("--(")).ToList();
            if (STList.Count() != 0 && LadderList.Count() != 0 && (STList.Count() == 2 || STList.Count() % 2 == 0))
            {
                mylogic.Add("Network # " + networknumber + ": can't be converted:Mixed structured Text and Ladder is not supported in this Version");
                returnvalue = true;
            }
            return returnvalue;
        }

        private static void calculateParrallelSize(SafetyTemplate mynewSafetyTemplate)
        {
            coil mylast = mynewSafetyTemplate.SafetycoilMap.LastOrDefault().Value;
            if (mylast != null)
            {
                List<string> splitedparrallellogic = mylast.Parrallelcoilstring.Split('/').Where(x => x != "").ToList();
                int par = splitedparrallellogic.Count() / 2;
                mynewSafetyTemplate.Networkcount = mynewSafetyTemplate.Networkcount + par;
            }
        }

        private static void calculateDescrptionSize(SafetyTemplate mynewSafetyTemplate, List<string> networkDiscption)
        {
            double des = networkDiscption.Count() / 6.0;
            for (int i = 0; i < des; i++)
            {
                mynewSafetyTemplate.Networkcount++;
            }
        }

        private static bool SerializePack(List<string> list, ref string logic, ref int begin, int v, ref SafetyTemplate mynewSafetyTemplate, ref SortedDictionary<int, int> iDS, ref List<contact> mycontactlist)
        {
            bool returnvalue = false;
            int comulativeI = 0;
            var ComundList = list.Select((Value, Index) => new { Value, Index })
                .Where(p => p.Value.Contains("PACK") && p.Value.Contains("=") && p.Value != "" && p.Value.Contains("(")).ToList();

            string intiallogic = "";
            string command = "";
            string register = "";
            foreach (var comm in ComundList)
            {
                List<string> packlist = list.ToList().GetRange(comm.Index, list.Count() - (comm.Index));
                int Commaindex = comm.Value.ToUpper().IndexOf(",");
                int practisindex = comm.Value.ToUpper().IndexOf("(");

                register = comm.Value.Substring(comm.Value.ToUpper().IndexOf("(") + 1, (comm.Value.ToUpper().IndexOf(",") - comm.Value.ToUpper().IndexOf("(")) - 1).Trim().Replace("-|", "").Replace("|", "").Replace("-", "_");
                int lowerIndex = comm.Index - 1;

                int i = 0;

                foreach (var tag in packlist)
                {
                    command = command + tag.Trim().Replace("-|", "").Replace("|", "").Replace(" ", "").Replace("-", "_").Replace("+", "").Replace(";", "");
                    if (tag.Contains(";"))
                    {
                        break;
                    }
                }
                comulativeI++;
            }
            if (register != "")
            {
                logic = command.Replace(register + ",", "").Replace("wOUTPUT", register);
                mynewSafetyTemplate.Name = register + " PACK/UNPACK";
                mynewSafetyTemplate.Template = logic;
                returnvalue = true;
            }
            return returnvalue;
        }

        private static List<string> SerializeDescription(List<string> list)
        {
            var CompoundList = list.Select((Value, Index) => new { Value, Index })
                .Where(p => p.Value.Contains("+===") && p.Value.Contains("==+")).ToList();
            List<string> Discptionlist = new List<string>();
            if ((CompoundList != null) && (CompoundList.Count() != 0))
            {
                var thisCompoundFirst = CompoundList.FirstOrDefault();
                var thisCompoundLast = CompoundList.LastOrDefault();
                int upperIndex = thisCompoundLast.Index;

                int lowerIndex = thisCompoundFirst.Index + 1;
                Discptionlist = list.ToList().GetRange(lowerIndex, (upperIndex - lowerIndex));
            }
            Discptionlist = RemoveDescrptionSeperators(Discptionlist);

            return Discptionlist;
        }

        private static List<string> RemoveDescrptionSeperators(List<string> discptionlist)
        {
            List<string> moddescrption = new List<string>();
            foreach (var line in discptionlist)
            {
                var myline = line.Trim();
                // myline = myline.Replace("|", "").Trim();
                int sepindex = myline.IndexOf("|") + 1;
                myline = myline.Substring(sepindex, myline.Length - sepindex).Trim();
                // string reverse = myline.Reverse().ToString();
                // sepindex = myline.Reverse().ToString().IndexOf("|");
                try
                {
                    myline = myline.Substring(0, myline.Length - 1).Trim();
                }
                catch(Exception ex)
                {
                    string tt = ex.Message;
                }
                
                moddescrption.Add(myline);
            }
            return moddescrption;
        }

        private static void Checkbugexist(ref SafetyTemplate mynewSafetyTemplate, string logic)
        {
            if (logic.Contains("="))
            {
                string[] parrelLit = logic.Split('=');
                if (parrelLit.Length >= 3)
                {
                    coil mycoil = mynewSafetyTemplate.SafetycoilMap.FirstOrDefault(x => x.Value.Name == parrelLit[1]).Value;
                    if (mycoil != null)
                    {
                        DeleteExtraOr(ref mycoil);
                    }
                }
            }
        }

        private static void DeleteExtraOr(ref coil mycoil)
        {
            string[] parrelLit = mycoil.Logic.Split('=');
            string logic = parrelLit[parrelLit.Length - 1];
            if (logic.Length > 3 && logic.Substring(0, 3).ToUpper() == "OR(")
            {
                logic = logic.Substring(3, logic.Length - 4);

                mycoil.Logic = mycoil.Name + "=" + logic;
            }
        }

        private static void SerializeLogic(List<string> list, ref string logic)
        {
            int comulativeI = 0;
            var ComundList = list.Select((Value, Index) => new { Value, Index })
                .Where(p => p.Value.Contains("]")).ToList();
            string intiallogic = "";
            foreach (var comm in ComundList)
            {
                int lowerIndex = comm.Index - 1;

                int i = 0;

                foreach (var mych in comm.Value)
                {
                    switch (mych)
                    {
                        case ']':
                            GetName(list[lowerIndex], i, ref intiallogic);

                            intiallogic = intiallogic.Replace(",,", ",");
                            break;

                        case '+':
                            if (comulativeI != ComundList.Count())
                                intiallogic = "OR(" + intiallogic;
                            if (intiallogic.Contains("OR(OR"))
                            {
                                intiallogic = intiallogic.Replace("OR(OR", "OR");
                            }
                            break;

                        case ' ':
                            break;

                        case '-':
                            if (i == 9)
                            {
                                string logicneeded = Getlogic(comm.Value, i, intiallogic);
                                intiallogic = logicneeded + intiallogic + ",";
                            }
                            break;

                        case '(':
                            intiallogic = intiallogic + ")";
                            string coilname = "";
                            // GetCoilName(list[lowerIndex], ref intiallogic,ref coilname);
                            break;
                    }
                    i++;
                }
                comulativeI++;
            }
            logic = intiallogic.Trim();
        }

        private static string Getlogic(string ladderstring, int start, string previouslogic)
        {
            string logic = "";
            char mych = ' ';
            for (int k = start; k < ladderstring.Length; k++)
            {
                mych = ladderstring[k];
                if (mych == '+' || mych == ']' || mych == ' ')
                {
                    break;
                }
            }
            string[] stringSeparators = new string[] { "]" };
            string[] conttactsNamesLit = ladderstring.Split(stringSeparators, StringSplitOptions.None);

            string lastlogic = "";
            for (int k = previouslogic.Length - 1; k > 0; k--)
            {
                char mychp = previouslogic[k];
                if (mychp == '(')
                {
                    string ortest = previouslogic[k - 2].ToString() + previouslogic[k - 1].ToString();
                    if (ortest == "OR")
                    {
                        lastlogic = "OR";
                    }
                    if (ortest == "ND")
                    {
                        lastlogic = "AND";
                    }
                    break;
                }
                if (mychp == ')')
                {
                    break;
                }
            }

            switch (mych)
            {
                case ']':
                    // if(lastlogic!="AND" && conttactsNamesLit.Count()>2 &&!(conttactsNamesLit.Count()==3 && ladderstring.Contains("+  ") ) )
                    String prv = "";
                    if (start - 2 > 0)
                    {
                        prv = ladderstring[start - 2].ToString() + ladderstring[start - 1].ToString();
                    }
                    //  if (lastlogic != "AND" && conttactsNamesLit.Count() > 2 && (prv != " +"))
                    if (lastlogic != "AND" && conttactsNamesLit.Count() > 2)
                    {
                        logic = "AND(";
                    }
                    //if(conttactsNamesLit.Count() == 3 && ladderstring.Contains("+  "))
                    //  {
                    //        logic = "OR(";
                    //    }

                    break;

                case '+':

                    //if (lastlogic != "OR")
                    //{
                    //    logic = "OR(";
                    //    // logic = "";
                    //}

                    break;
            }
            return logic;
        }

        private void SerializeLogicMod(List<string> list, ref string logic, ref int posbegin, int posend, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> IDS, ref List<contact> mycontactlist, ref List<coil> mycoillist, ref List<string> Library, DataTable myDB, List<string> NetworkDiscption, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, string networknumber)
        {
            int comulativeI = 0;
            string Lastcommonlogic = logic;
            string logicneeded = "";

            Dictionary<int, List<int>> myhis = new Dictionary<int, List<int>>();

            var ComundList = list.Select((Value, Index) => new StringwithIndex { Value = Value, Index = Index })
                .Where(p => p.Value.Contains("--]") || p.Value.Contains("--(")).ToList();

            string OrLogic = "";
            string intiallogic = logic.Trim();
            string Parreleloutputs = "";
            int posbeginparralel = 0;
            foreach (var comm in ComundList)
            {
                int lowerIndex = comm.Index - 1;

                int i = 0;
                string contactname = "";
                string modcontactname = "";
                foreach (var mych in comm.Value)
                {
                    if (i > posbegin && i <= posend && Checkcharhistory(comm.Index, i, myhis))
                    {
                        AddCharhistory(comm.Index, i, myhis);
                        switch (mych)
                        {
                            case ']':
                                GetName(list[lowerIndex], i, ref modcontactname);

                                //char contacttype = comm.Value[i + 1];
                                string contctfn = comm.Value.Substring(i, 4);
                                string contacttype = contctfn.Substring(contctfn.IndexOf(']') + 1, (contctfn.IndexOf('[') - contctfn.IndexOf(']')) - 1);
                                string contactsetpoint = "";
                                if ((contacttype.Contains(">") || contacttype.Contains("<") || contacttype.Contains("==")) && list.Count() > lowerIndex + 2)
                                {
                                    GetName(list[lowerIndex + 2], i, ref contactsetpoint);
                                }

                                contactname = GetContactFN(modcontactname, comm.Value.Substring(i, 4), contactsetpoint);

                                AddNewContact(modcontactname, ref myTemplate, ref IDS, ref mycontactlist, contacttype, contactsetpoint, networknumber);

                                // intiallogic = intiallogic.Replace(",,", ",");
                                break;

                            case '+':
                                if (i != posbegin + 1)
                                {
                                    var parrallelogictestOLD = comulativeI + 1 < ComundList.Count() && ComundList[comulativeI + 1].Value.Length >= i && (((i > 0 && comm.Value[i - 1] == '-') || i == 0) && ((ComundList[comulativeI + 1].Value[i - 1] == '-' && ComundList[comulativeI + 1].Value[i] == '+') || (ComundList[comulativeI + 1].Value[i] == '|')));
                                    var parrallelogictest = comulativeI + 1 < ComundList.Count() && ComundList[comulativeI + 1].Value.Length >= i && (((ComundList[comulativeI + 1].Value[i - 1] == '-' && ComundList[comulativeI + 1].Value[i] == '+') || (ComundList[comulativeI + 1].Value[i] == '|')));
                                    if (comulativeI + 1 != ComundList.Count() && parrallelogictest)
                                    {
                                        SetOringlogic(ref OrLogic, ref intiallogic, contactname);
                                    }
                                    else
                                    {
                                        // SetOringlogic(ref OrLogic, ref intiallogic, contactname);
                                        if (contactname != "")
                                        {
                                            intiallogic = AddcontacttoLogic(intiallogic, contactname);
                                            if (!parrallelogictest)
                                            {
                                                // OrLogic= AddcontacttoLogic(OrLogic, contactname);
                                                //  UpdateLogicWcompletepath(ref OrLogic, ref intiallogic, contactname);

                                                if (intiallogic == "")
                                                {
                                                    intiallogic = OrLogic;
                                                }
                                                //  SetOringlogic(ref OrLogic, ref intiallogic, contactname);

                                                OrLogic = "";
                                            }
                                        }
                                        else
                                        {
                                            if (comulativeI + 1 == ComundList.Count())
                                            {
                                                // intiallogic = CheckORwoneInput(intiallogic);//tamer may be big error
                                            }
                                            //NEW ADDED FOR LAST OR BLANK
                                        }
                                    }
                                    // SetOringlogic(ref OrLogic, ref intiallogic, contactname);
                                    contactname = "";
                                    string outputremainlogicp, lastinputsp, lastlogicNEWp = ""; int inputpinp = 0;
                                    Analaizelogicstatement(intiallogic, ref lastlogicNEWp, out outputremainlogicp, out lastinputsp, out inputpinp);
                                    CheckSpecialOring(posbeginparralel, i, list, comm, ComundList, ref intiallogic);
                                    if (intiallogic.Contains("OR(OR") && (outputremainlogicp != "" && lastlogicNEWp != "OR("))
                                    {
                                        intiallogic = intiallogic.Replace("OR(OR", "OR");
                                    }

                                    if (parrallelogictest)
                                    {
                                        List<int> myindex = new List<int>();
                                        List<int> myremainindex = new List<int>();
                                        List<string> myremain = new List<string>();
                                        string expectedremainlogic = "";
                                        int copyposbeginparralel = posbeginparralel;
                                        if (posbegin > posbeginparralel)
                                        {
                                            copyposbeginparralel = posbegin;
                                        }
                                        // List<string> myparralelList = GetParrallellist(list, comm.Index, posbeginparralel, i, ref myindex, ref myremain, ref myremainindex, ref expectedremainlogic);
                                        List<string> myparralelList = GetParrallellist(list, comm.Index, copyposbeginparralel, i, ref myindex, ref myremain, ref myremainindex, ref expectedremainlogic);
                                        //   SerializeLogicMod(list.ToList().GetRange(ComundList[comulativeI + 1].Index - 1, list.Count() - (ComundList[comulativeI + 1].Index - 1)), ref intiallogic, ref posbegin, i, ref myTemplate, ref IDS, ref mycontactlist, ref Library, myDB,NetworkDiscption,ref newSafetyTemplateMap, networknumber);
                                        //SerializeLogicMod(myparralelList, ref intiallogic, ref posbeginparralel, i, ref myTemplate, ref IDS, ref mycontactlist, ref mycoillist, ref Library, myDB, NetworkDiscption, ref newSafetyTemplateMap, networknumber);
                                        SerializeLogicMod(myparralelList, ref intiallogic, ref copyposbeginparralel, i, ref myTemplate, ref IDS, ref mycontactlist, ref mycoillist, ref Library, myDB, NetworkDiscption, ref newSafetyTemplateMap, networknumber);
                                        Updatehistory(myhis, myindex, posbeginparralel, i);
                                        string remainlogic = "";
                                        if (myremain.Count() != 0 && expectedremainlogic == "OR")
                                        {
                                            SerializeLogicMod(myremain, ref remainlogic, ref posbegin, i, ref myTemplate, ref IDS, ref mycontactlist, ref mycoillist, ref Library, myDB, NetworkDiscption, ref newSafetyTemplateMap, networknumber);
                                            if (remainlogic != "")
                                            {
                                                ReachEndOfLogic(ref remainlogic);
                                                ReachEndOfLogic(ref intiallogic);
                                                intiallogic = AddjustAddingNewLogic(intiallogic, "OR", remainlogic);
                                            }
                                            else
                                            {
                                            }
                                            Updatehistory(myhis, myremainindex, posbegin, i);
                                        }
                                    }
                                    else
                                    {
                                        if (comulativeI + 1 < ComundList.Count() && ((ComundList[comulativeI + 1].Value.Length >= i && (ComundList[comulativeI + 1].Value[i] != '+' || ComundList[comulativeI + 1].Value[i - 1] == ' ')) || ComundList[comulativeI + 1].Value.Length < i))
                                        {
                                            // intiallogic = intiallogic + ")";

                                            if (posend - i <= 1)
                                            {
                                                intiallogic = CheckORwoneInput(intiallogic);//STOP THIS IF REQUIRED
                                                OrLogic = CheckORwoneInput(OrLogic);
                                                intiallogic = manualedit(intiallogic);
                                                OrLogic = manualedit(OrLogic);
                                                ReachEndOfLogic(ref intiallogic);
                                                ReachEndOfLogic(ref OrLogic);
                                            }
                                        }
                                        // intiallogic = CheckORwoneInput(intiallogic);//hasanin check
                                    }
                                    intiallogic = manualedit(intiallogic);
                                    // posbegin = i;
                                    posbeginparralel = i;
                                    Lastcommonlogic = manualedit(intiallogic);
                                    if (logicneeded != "" && Lastcommonlogic.Length > logicneeded.Length && logicneeded != "OR(")
                                    {
                                        // Lastcommonlogic = "AND(OR(H4216X1, H4216OP)";
                                        if (Lastcommonlogic.Substring(0, logicneeded.Length) == logicneeded && !Lastcommonlogic.EndsWith(")"))//NEED TO CHECK END WITH )
                                        {
                                            //  Lastcommonlogic = Lastcommonlogic.Substring(logicneeded.Length, Lastcommonlogic.Length - logicneeded.Length);
                                            Lastcommonlogic = CheckORwoneInput(Lastcommonlogic);
                                        }
                                    }
                                }
                                else
                                {
                                    OrLogic = "";
                                }
                                intiallogic = manualedit(intiallogic);
                                //if (posend - i <= 1 || comulativeI + 1 == ComundList.Count())
                                string outputremainlogic, lastinputs, lastlogicNEW = ""; int inputpin = 0;
                                Analaizelogicstatement(intiallogic, ref lastlogicNEW, out outputremainlogic, out lastinputs, out inputpin);
                                if (posend - i <= 1 && comulativeI + 1 == ComundList.Count() && lastlogicNEW.Contains("OR") && (Getlogicneeded(posbegin, posend, ComundList[comulativeI].Value) == "AND(" || contactname == ""))//tamer may be big error//TOCHECK LAST LOGIC IS AND
                                {
                                    //  intiallogic = CheckORwoneInput(intiallogic);//THIS LOGIC FOR PATH CONTAIN AND AT LAST STEP LADDER
                                    intiallogic = intiallogic + ")";
                                    //ReachEndOfLogic(ref intiallogic);
                                    //ReachEndOfLogic(ref OrLogic);
                                }
                                //
                                break;

                            case ' ':
                                break;

                            case '-':
                                if ((i % 10 == 0) && ((comm.Value.Substring(i, comm.Value.Length - i).Contains("["))))
                                // if ((i % 10 == 0))
                                {
                                    logicneeded = Getlogic(comm.Value, i, intiallogic);

                                    if ((intiallogic == "") && contactname != "")
                                    {
                                        intiallogic = logicneeded + contactname + ", ";
                                        OrLogic = logicneeded + contactname + ", ";
                                    }
                                    else
                                    {
                                        if (contactname != "")
                                        {
                                            //intiallogic = intiallogic +logicneeded + contactname + ",";

                                            intiallogic = AddjustAddingNewLogic(intiallogic, logicneeded, contactname);
                                            OrLogic = AddjustAddingNewLogic(OrLogic, logicneeded, contactname);
                                        }
                                        else
                                        {
                                            string lastlogic = GetlastLogic(ref intiallogic);
                                            if (!lastlogic.EndsWith("("))
                                            {
                                                lastlogic = lastlogic + "(";
                                            }
                                            if ((logicneeded != "") && (intiallogic != ""))
                                            {
                                                if (logicneeded == "AND(" && !lastlogic.Contains("OR") && logicneeded != lastlogic)
                                                {
                                                    intiallogic = logicneeded + intiallogic + ",";
                                                }
                                            }

                                            //  else
                                            //{
                                            //    intiallogic = intiallogic + logicneeded + ",";
                                            //}

                                            if ((logicneeded != "") && (OrLogic != ""))
                                            {
                                                if (logicneeded == "AND(" && !lastlogic.Contains("OR") && logicneeded != lastlogic)
                                                {
                                                    OrLogic = logicneeded + OrLogic + ",";
                                                }
                                                //else
                                                //{
                                                //    OrLogic = OrLogic + logicneeded + ",";
                                                //}
                                            }
                                        }
                                    }

                                    string outputremainlogicp, lastinputsp, lastlogicNEWp = ""; int inputpinp = 0;
                                    Analaizelogicstatement(intiallogic, ref lastlogicNEWp, out outputremainlogicp, out lastinputsp, out inputpinp);
                                    if (outputremainlogicp == "" && lastlogicNEWp == "" && intiallogic != "")
                                    {
                                        if (intiallogic.Length > 2 && intiallogic.Substring(0, 2) != "OR")
                                        {
                                            intiallogic = "AND(" + intiallogic;
                                        }
                                        else
                                        {
                                            //if (intiallogic.Length > 2 && intiallogic.Substring(0, 2) == "OR")
                                            //{
                                            //    intiallogic = "OR(" + intiallogic;
                                            //}
                                        }
                                    }
                                }
                                else
                                {
                                    if ((i % 10 == 0))
                                    {
                                        //  intiallogic = intiallogic + "," + contactname;
                                        if ((intiallogic == "") && contactname != "")
                                        {
                                            intiallogic = contactname;
                                            OrLogic = intiallogic;
                                        }
                                        else
                                        {
                                            if (contactname != "")
                                            {
                                                intiallogic = intiallogic.Trim();
                                                OrLogic = OrLogic.Trim();
                                                if (!intiallogic.EndsWith(","))
                                                {
                                                    intiallogic = intiallogic + "," + contactname;
                                                    OrLogic = OrLogic + "," + contactname;
                                                }
                                                else
                                                {
                                                    intiallogic = intiallogic + contactname;
                                                    OrLogic = OrLogic + contactname;
                                                }
                                            }
                                        }
                                    }
                                }
                                if (i % 10 == 0)
                                {
                                    if (comm.Value.Substring(i, comm.Value.Length - i).Contains("["))
                                    {
                                        contactname = "";
                                    }
                                    contactname = "";
                                    if (!intiallogic.EndsWith("),"))
                                    {
                                        // intiallogic = intiallogic.Replace("AND(AND(", "AND(");
                                    }

                                    if (!OrLogic.EndsWith("),"))
                                    {
                                        // OrLogic = OrLogic.Replace("AND(AND(", "AND(");
                                    }
                                }

                                break;

                            case '(':
                                if (intiallogic == "" && contactname != "")
                                {
                                    intiallogic = contactname;
                                }
                                else
                                {
                                    intiallogic = manualedit(intiallogic);
                                    OrLogic = manualedit(OrLogic);
                                    intiallogic = CheckORwoneInputinlastlogic(intiallogic);
                                    ReachEndOfLogic(ref intiallogic);
                                    ReachEndOfLogic(ref OrLogic);
                                    // intiallogic = intiallogic + ")";//study
                                }
                                string coilname = "";
                                string timer = "";
                                string coiltype = comm.Value.Substring(i + 1, (comm.Value.IndexOf(")") - i) - 1).Replace(" ", "");
                                if (coiltype != "" && coiltype != "NOT" && coiltype != "RST")
                                {
                                    int timeIndex = comm.Index + 1;

                                    try
                                    {
                                        GetName(list[timeIndex], i, ref timer);
                                        // timer = list[timeIndex].Replace(" ", "").Replace("", "").Replace("|", "");
                                    }
                                    catch (Exception e)
                                    {
                                        string tt = e.Message;
                                    }
                                }
                                if (intiallogic == "")
                                {
                                    intiallogic = "TRUE";
                                }
                                GetCoilName(list[lowerIndex], ref intiallogic, ref coilname, coiltype, timer, mycoillist);
                                bool paraleellogic = true;
                                if (Parreleloutputs == "")
                                {
                                    Parreleloutputs = coilname + "[" + coiltype + "]";
                                }
                                else
                                {
                                    // Parreleloutputs = coilname + "/" + Parreleloutputs;
                                    NetworkDiscption.Clear();
                                    if (paraleellogic = !CheckIsParrallelLogic(ref Parreleloutputs, myTemplate, intiallogic, coilname, coiltype))
                                    {
                                        createnewcontactlist(ref mycontactlist, ref myTemplate, ref IDS);
                                    }
                                }
                                //if (coilname.Contains("U206AK") || coilname.Contains("SOE_GO"))
                                //{
                                //}
                                //if(intiallogic.Trim().EndsWith("="))
                                //{
                                //    intiallogic = intiallogic + "1";

                                //}

                                coil mycoil = AddNewCoil(coilname, ref myTemplate, intiallogic, ref IDS, mycontactlist, timer, coiltype, Parreleloutputs, NetworkDiscption, networknumber);
                                // ADDLatchinglogic(mycoil, newSafetyTemplateMap);
                                if ((myTemplate.Networkcount > MAXNETWORKCOUNT && !SpacerUse) && paraleellogic)
                                {
                                    AddnewTemplate(ref myTemplate, newSafetyTemplateMap, ref IDS);
                                }
                                //if (coiltype == "LCH" || coiltype == "RST" || coiltype == "TMR"|| coiltype == "CTU" )
                                //{
                                //    mycoillist.Add(mycoil);
                                //    AdjustLocgicForLCH(coilname, ref myTemplate, ref newSafetyTemplateMap, ref IDS, mycoillist, timer, coiltype, NetworkDiscption, networknumber);
                                //    //if(paraleellogic)
                                //    //{
                                //    //    myTemplate.Networkcount++;
                                //    //}
                                //}
                                if (comulativeI + 1 < ComundList.Count())
                                {
                                    intiallogic = Lastcommonlogic;
                                    intiallogic = CheckORwoneInput(intiallogic);
                                }

                                break;
                        }
                    }
                    i++;
                }
                //if(comm.Value.Length<=posbegin+1 && i==posend)
                //{
                //}
                comulativeI++;
            }

            //   ReachEndOfLogic(ref intiallogic);
            logic = intiallogic.Trim();
            //if(logic.Contains("="))//study
            //{
            //    logic = logic + ")";
            //}
            logic = manualedit(logic);
        }

        private void CheckSpecialOring(int posbeginparralel, int end, List<string> list, StringwithIndex comm, List<StringwithIndex> comundList, ref string intiallogic)
        {
            if (posbeginparralel > 0 && end > 0 && end > posbeginparralel && comm.Value.Length >= end)
            {
                string substring = comm.Value.Substring(posbeginparralel, end - posbeginparralel);
                var remain = comundList.Where(x => x.Index > comm.Index).ToList();
                if (!substring.Contains("]") && substring.Contains("-"))
                {
                    string outputremainlogicp, lastinputsp, lastlogicNEWp = ""; int inputpinp = 0;
                    Analaizelogicstatement(intiallogic, ref lastlogicNEWp, out outputremainlogicp, out lastinputsp, out inputpinp);
                    if (outputremainlogicp == "" && lastlogicNEWp == "" && intiallogic != "")
                    {
                        List<int> myremainindex = new List<int>();
                        List<string> myremain = new List<string>();
                        List<int> myindex = new List<int>();
                        string expectedremainlogic = "";
                        List<string> myparrallel = GetParrallellist(list, comm.Index, posbeginparralel, end, ref myindex, ref myremain, ref myremainindex, ref expectedremainlogic);
                        if (myparrallel.Count() > 0 || myremain.Count() > 0)
                        {
                            if (checkthereiscontactInRangeFortwoList(myparrallel, myremain, posbeginparralel, end))
                            {
                                intiallogic = "OR(" + intiallogic + ",";
                            }
                        }
                    }
                }
            }
        }

        private bool checkthereiscontactInRangeFortwoList(List<string> myparrallel, List<string> myremain, int posbeginparralel, int end)
        {
            return checkthereiscontactInRange(myparrallel, posbeginparralel, end) || checkthereiscontactInRange(myremain, posbeginparralel, end);
        }

        private static bool checkthereiscontactInRange(List<string> myparrallel, int posbeginparralel, int end)
        {
            bool returnvalue = false;
            foreach (string comm in myparrallel)
            {
                if (posbeginparralel > 0 && end > 0 && end > posbeginparralel && comm.Length >= end)
                {
                    string substring = comm.Substring(posbeginparralel, end - posbeginparralel);

                    if (substring.Contains("]") && substring.Contains("-"))
                    {
                        returnvalue = true;
                        break;
                    }
                }
            }
            return returnvalue;
        }

        private void CheckSpecialOring(int posbeginparralel, int end, string linestring, ref string intiallogic)
        {
            if (posbeginparralel > 0 && end > 0 && end > posbeginparralel && linestring.Length >= end)
            {
                string substring = linestring.Substring(posbeginparralel, end - posbeginparralel);
                if (!substring.Contains("]") && substring.Contains("-"))
                {
                    string outputremainlogicp, lastinputsp, lastlogicNEWp = ""; int inputpinp = 0;
                    Analaizelogicstatement(intiallogic, ref lastlogicNEWp, out outputremainlogicp, out lastinputsp, out inputpinp);
                    if (outputremainlogicp == "" && lastlogicNEWp == "" && intiallogic != "")
                    {
                        intiallogic = "OR(" + intiallogic + ",";
                    }
                }
            }
        }

        private void CheckNameForLCH(ref string coilname, List<coil> mycoillist)
        {
            string mycoilcopy = coilname;
            var mycoil = mycoillist.FirstOrDefault(cl => cl.Name == mycoilcopy);
            if (mycoil != null)
            {
                int i = 2;
                while (mycoil != null)
                {
                    mycoilcopy = coilname + "_" + i.ToString().PadLeft(2, '0');
                    mycoil = mycoillist.FirstOrDefault(cl => cl.Name == mycoilcopy);
                    i++;
                }
            }
            coilname = mycoilcopy;
        }

        private void AdjustLocgicForLCH(string coilname, ref SafetyTemplate myTemplate, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref SortedDictionary<int, int> iDS, List<coil> mycoillist, string timer, string coiltype, List<string> networkDiscption, string networknumber)
        {
            // string orgcoilname = coilname.Replace("_LCH","").Replace("_RST", "");
            //  coilname = coilname + "_" + coiltype;
            string[] stringcoiltypes = new string[] { "_LCH", "_RST", "", "_TMR", "_CTU" };

            string orgcoilname = coilname.Split(stringcoiltypes, StringSplitOptions.None)[0];
            //  var othercoil = myTemplate.SafetycoilMap.Values.Where(x => x.Name.Contains(orgcoilname+"_")).OrderBy(x=>x.Coiltype);
            var othercoil = mycoillist.Where(x => x.Name.Contains(orgcoilname) && x.Coiltype != coiltype && x.Coiltype != "").OrderBy(x => x.Coiltype).ToList();
            if (othercoil != null && othercoil.Count() != 0)

            {
                //foreach (var mycoil in othercoil)
                //{
                string mycoiltype = othercoil.FirstOrDefault().Coiltype;
                if (stringcoiltypes.Contains("_" + mycoiltype) && stringcoiltypes.Contains("_" + coiltype))
                {
                    var minecoils = mycoillist.Where(x => x.Name.Contains(orgcoilname) && x.Coiltype == coiltype).OrderBy(x => x.Name).ToList();
                    string sRcoil = orgcoilname;
                    //  string logic = orgcoilname + "=SR(" + orgcoilname + "_LCH" + "," + orgcoilname + "_RST)";
                    string logic = "";
                    var orgcoils = mycoillist.Where(x => x.Name == orgcoilname).ToList();
                    if (orgcoils != null && orgcoils.Count() != 0)
                    {
                        Deletcoil(orgcoilname, newSafetyTemplateMap);
                    }
                    coil mycoilorg = Addcoil(ref orgcoilname, myTemplate, ref logic, iDS, "", "", networkDiscption, networknumber);
                    logic = GetlogicofLCH(othercoil, minecoils, ref mycoilorg, ref myTemplate, ref iDS);

                    mycoillist.Add(mycoilorg);

                    myTemplate.Networkcount++;
                }
                // }
            }
            if (coiltype == "RST")
            {
                CheckTwinCoilRST(orgcoilname, ref myTemplate, newSafetyTemplateMap);
            }

            if (coiltype == "LCH" || coiltype == "TMR" || coiltype == "CTU")
            {
                UpdateTwinCoilRST(orgcoilname, ref myTemplate, newSafetyTemplateMap);
            }
        }

        private void UpdateTwinCoilRST(string orgcoilname, ref SafetyTemplate myTemplate, Dictionary<string, SafetyTemplate> newSafetyTemplateMap)
        {
            if (orgcoilname == "TX015SS")
            {
            }
            coil lchcoil = myTemplate.SafetycoilMap.Values.FirstOrDefault(x => x.Name == orgcoilname + "_LCH" || x.Name == orgcoilname + "_TMR" || x.Name == orgcoilname + "_CTU");
            coil lchcoilinother = CheckLCHInOtherTemplate(orgcoilname, newSafetyTemplateMap);
            if (lchcoil != null || lchcoilinother != null)
            {
                UpdateTwinCoilType(orgcoilname, myTemplate, newSafetyTemplateMap);
            }
        }

        private static void UpdateTwinCoilType(string orgcoilname, SafetyTemplate myTemplate, Dictionary<string, SafetyTemplate> newSafetyTemplateMap)
        {
            foreach (SafetyTemplate mytemplate in newSafetyTemplateMap.Values.Where(x => x.SafetycoilMap.Values.Where(y => y.Name.Contains(orgcoilname + "_RST")).FirstOrDefault() != null))
            {
                foreach (var rstcoil in mytemplate.SafetycoilMap.Where(x => x.Value.Name.Contains(orgcoilname + "_RST")).ToList())
                {
                    rstcoil.Value.twinCoiltype = "LCH";
                }
            }

            foreach (var rstcoil in myTemplate.SafetycoilMap.Where(x => x.Value.Name.Contains(orgcoilname + "_RST")).ToList())
            {
                rstcoil.Value.twinCoiltype = "LCH";
            }
        }

        private coil CheckLCHInOtherTemplate(string orgcoilname, Dictionary<string, SafetyTemplate> newSafetyTemplateMap)
        {
            coil mycoil = null;
            foreach (SafetyTemplate mytemplate in newSafetyTemplateMap.Values.Where(x => x.SafetycoilMap.Values.Where(y => y.Name == orgcoilname + "_LCH" || y.Name == orgcoilname + "_TMR" || y.Name == orgcoilname + "_CTU").FirstOrDefault() != null))
            {
                mycoil = mytemplate.SafetycoilMap.Values.FirstOrDefault(x => x.Name == orgcoilname + "_LCH" || x.Name == orgcoilname + "_TMR" || x.Name == orgcoilname + "_CTU");
                break;
            }
            return mycoil;
        }

        private void CheckTwinCoilRST(string orgcoilname, ref SafetyTemplate myTemplate, Dictionary<string, SafetyTemplate> newSafetyTemplateMap)
        {
            coil rstcoil = myTemplate.SafetycoilMap.Values.FirstOrDefault(x => x.Name.Contains(orgcoilname + "_RST"));

            if (rstcoil != null)
            {
                var lchcoil = myTemplate.SafetycoilMap.Values.FirstOrDefault(x => x.Name == orgcoilname + "_LCH" || x.Name == orgcoilname + "_TMR" || x.Name == orgcoilname + "_CTU") != null ? myTemplate :

                     newSafetyTemplateMap.Values.FirstOrDefault(z => z.SafetycoilMap.Values.FirstOrDefault(k => k.Name == orgcoilname + "_LCH" || k.Name == orgcoilname + "_TMR" || k.Name == orgcoilname + "_CTU") != null);
                if (lchcoil != null)
                {
                    foreach (var crstcoil in myTemplate.SafetycoilMap.Where(x => x.Value.Name.Contains(orgcoilname + "_RST")).ToList())
                    {
                        crstcoil.Value.twinCoiltype = "LCH";
                    }
                }
            }
            else
            {
                var tt = newSafetyTemplateMap.Values.FirstOrDefault(z => z.SafetycoilMap.Values.FirstOrDefault(k => k.Name == orgcoilname + "_LCH" || k.Name == orgcoilname + "_TMR" || k.Name == orgcoilname + "_CTU") != null);
                if (tt != null)
                {
                    foreach (SafetyTemplate mytemplate in newSafetyTemplateMap.Values.Where(x => x.SafetycoilMap.Values.Where(y => y.Name.Contains(orgcoilname + "_RST")).FirstOrDefault() != null))
                    {
                        foreach (var crstcoil in mytemplate.SafetycoilMap.Where(x => x.Value.Name.Contains(orgcoilname + "_RST")).ToList())
                        {
                            crstcoil.Value.twinCoiltype = "LCH";
                        }
                    }
                }
            }
        }

        private void Deletcoil(string orgcoilname, Dictionary<string, SafetyTemplate> newSafetyTemplateMap)
        {
            foreach (SafetyTemplate mytemplate in newSafetyTemplateMap.Values.Where(x => x.SafetycoilMap.Values.Where(y => y.Name == orgcoilname).FirstOrDefault() != null))
            {
                foreach (var removedcoil in mytemplate.SafetycoilMap.Where(x => x.Value.Name == orgcoilname).ToList())
                {
                    foreach (var removedcontact in removedcoil.Value.MyLogic.LogiccontactMap)
                    {
                        mytemplate.SafetycontactMap.Remove(removedcontact.Value.localId);
                    }
                    mytemplate.SafetycoilMap.Remove(removedcoil.Key);
                }
            }
        }

        private static void AddcontactsforLachLogic(SafetyTemplate myTemplate, SortedDictionary<int, int> iDS, string orgcoilname, coil mycoil1)
        {
            contact mycon = AddContact(orgcoilname + "_LCH", myTemplate, iDS, "");
            contact mycon2 = AddContact(orgcoilname + "_RST", myTemplate, iDS, "");
            mycoil1.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            mycoil1.MyLogic.LogiccontactMap.Add(mycon2.localId, mycon2);
        }

        private string GetlogicofLCH(List<coil> othercoils, List<coil> minecoils, ref coil lchcoil, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> iDS)
        {
            string mine = "", other = "", minetype = "", othertype = "", returnvalue = "";

            other = ORingLCHTags(othercoils, ref lchcoil, ref myTemplate, ref iDS, ref othertype);

            mine = ORingLCHTags(minecoils, ref lchcoil, ref myTemplate, ref iDS, ref minetype);
            returnvalue = SPecifyGateLogic(mine, other, othertype, minecoils.FirstOrDefault().timer, othercoils.FirstOrDefault().timer);
            lchcoil.Logic = lchcoil.Name + "=" + returnvalue.Replace(",)", ")");
            if (returnvalue.Contains("TMR_I") || returnvalue.Contains("CTU("))
            {
                string timer = string.IsNullOrEmpty(othercoils.FirstOrDefault().timer) ? minecoils.FirstOrDefault().timer : othercoils.FirstOrDefault().timer;
                if (!CheckHexnumber(ref timer))
                {
                    contact mycon = AddContact(timer, myTemplate, iDS, "");
                    lchcoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
                }
            }
            return lchcoil.Logic;
        }

        private static string SPecifyGateLogic(string mine, string other, string othertype, string minetimer, string othertimer)
        {
            string returnvalue;
            if ((!other.Contains("TMR") && !mine.Contains("TMR")) && (!other.Contains("CTU") && !mine.Contains("CTU")))
            {
                if (othertype.Contains("RST"))
                {
                    returnvalue = "SR(" + mine + "," + other + ")";
                }
                else
                {
                    returnvalue = "RS(" + other + "," + mine + ")";
                }
            }
            else
            {
                if ((other.Contains("TMR") || mine.Contains("TMR")))
                {
                    if (othertype.Contains("RST"))
                    {
                        returnvalue = "TMR_I(" + mine + "," + other + "," + minetimer + ")";
                    }
                    else
                    {
                        returnvalue = "TMR_I(" + other + "," + mine + "," + othertimer + ")";
                    }
                }
                else
                {
                    if (othertype.Contains("RST"))
                    {
                        returnvalue = "CTU(" + mine + "," + other + "," + minetimer + ")";
                    }
                    else
                    {
                        returnvalue = "CTU(" + other + "," + mine + "," + othertimer + ")";
                    }
                }
            }

            return returnvalue;
        }

        private static string ORingLCHTags(List<coil> minecoils, ref coil lchcoil, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> iDS, ref string listtype)
        {
            string mine = "";
            if (minecoils.Count() == 1)
            {
                mine = minecoils.FirstOrDefault().Name;
                listtype = minecoils.FirstOrDefault().Coiltype;
                contact mycon = AddContact(mine, myTemplate, iDS, "");
                lchcoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            }
            else
            {
                foreach (coil mycoil in minecoils)
                {
                    if (mine == "")
                    {
                        listtype = mycoil.Coiltype;
                        mine = "OR(" + mycoil.Name + ",";
                    }
                    else
                    {
                        mine = mine + mycoil.Name + ",";
                    }
                    contact mycon = AddContact(mycoil.Name, myTemplate, iDS, "");
                    lchcoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
                }
            }
            if (minecoils.Count() > 1)
            {
                mine = (mine + ")").Replace(",)", ")");
            }
            return mine;
        }

        private string Getlogicneeded(int posbegin, int posend, string logicstring)
        {
            string returnvalue = "";
            if (logicstring.Length >= posbegin && logicstring.Length >= posend)
            {
                string partiallogic = logicstring.Substring(posbegin, posend - posbegin);

                if (partiallogic.Split(']').Count() > 2)
                {
                    returnvalue = "AND(";
                }
            }
            return returnvalue;
        }

        private static void SetOringl(List<object> comundList)
        {
            throw new NotImplementedException();
        }

        private static void Updatehistory(Dictionary<int, List<int>> myhis, List<int> myindex, int posbeginparralel, int end)
        {
            foreach (var index in myindex)
            {
                for (int i = posbeginparralel; i <= end; i++)
                {
                    AddCharhistory(index, i, myhis);
                }
            }
        }

        private static bool Checkcharhistory(int indexline, int charIndex, Dictionary<int, List<int>> myhis)
        {
            bool returnvalue = true;
            var myhischarList = myhis.FirstOrDefault(x => x.Key == indexline).Value;
            if (myhischarList != null && myhischarList.Contains(charIndex))
            {
                returnvalue = false;
            }
            return returnvalue;
        }

        private static void AddCharhistory(int indexline, int charIndex, Dictionary<int, List<int>> myhis)
        {
            var myhischarList = myhis.FirstOrDefault(x => x.Key == indexline).Value;
            if (myhischarList == null)
            {
                myhischarList = new List<int>();
                myhis.Add(indexline, myhischarList);
            }
            myhischarList.Add(charIndex);
        }

        private List<string> GetParrallellist(List<string> list, int startindex, int posbegin, int end, ref List<int> myindex, ref List<string> myremainforandOrOR, ref List<int> myremainforandOrORIndex, ref string expectedlogicforremaining)
        {
            List<int> mylist = new List<int>();
            myremainforandOrOR = new List<string>();
            myremainforandOrORIndex = new List<int>();
            int desclast = 0;

            var descList = list.Select((Value, Index) => new { Value, Index })
        .Where(p => p.Value.Contains("+===") && p.Value.Contains("==+")).ToList();
            if (descList != null && descList.Count > 0)
            {
                desclast = descList.LastOrDefault().Index;
            }

            var ComundList = list.Select((Value, Index) => new { Value, Index })
         .Where(p => ((p.Value.Contains("]") || p.Value.Contains("--(") || p.Value.Contains("|")) && p.Index > startindex) && p.Index > desclast).ToList();

            int i = 0;
            bool remain = false;
            foreach (var line in ComundList)
            {
                char myfirst = (line.Value.Length > posbegin) ? line.Value[posbegin] : ' ';
                char myendchar = (line.Value.Length > end) ? line.Value[end] : ' ';

                bool ParEndind = myendchar == '+' || myendchar == '|';
                bool ParStartind = myfirst == '+' || myfirst == '|' || posbegin == 0;
                string nextstring = line.Index != ComundList.LastOrDefault().Index ? ComundList[i + 1].Value : "";
                checkifneedToUpdateStartEnd(ref posbegin, ref end, myfirst, myendchar, line.Value, nextstring);
                if (ParStartind && ParEndind && !remain)
                {
                    if (i != 0)
                    {
                        mylist.Add(line.Index);
                    }
                }
                else
                {
                    if ((ParEndind == false && (ParStartind == false || posbegin == 0)) && remain == false)
                    {
                        break;
                    }
                    if (remain == false)
                    {
                        expectedlogicforremaining = GetshapeOfRemaining(ParStartind, ParEndind, list, line.Index, posbegin, end);
                    }
                    remain = true;
                }

                if (remain)
                {
                    myremainforandOrORIndex.Add(line.Index);
                }
                i++;
            }
            myindex = mylist;

            List<string> myreturn = GetListByindexforbothRemainAndParrellel(mylist, list, ref myremainforandOrOR, myremainforandOrORIndex);
            //List<string> myreturn = list.GetRange(mylist[1] - 1, (mylist[mylist.Count - 1] - (mylist[1] - 1)) + 1);
            return myreturn;
        }

        private void checkifneedToUpdateStartEnd(ref int posbegin, ref int posend, char myfirst, char myendchar, string value, string nextstring)
        {
            int begin = posbegin;
            int end = posend;
            char mynextendchar = (nextstring.Length > end) ? nextstring[end] : ' ';
            var charlist = value.Select((Value, Index) => new CharsIntring { Value = Value, Index = Index }).ToList();

            if (myfirst == '+' && charlist != null && charlist.Count > 0)
            {
                var checkedchar = charlist.Where(p => p.Index > begin).ToList();
                var pluschar = checkedchar.Where(p => p.Value == '+').ToList().FirstOrDefault();
                var contactchar = checkedchar.Where(p => p.Value == ']').ToList().FirstOrDefault();
                if (pluschar != null && contactchar != null && pluschar.Index < contactchar.Index)
                {
                    posbegin = pluschar.Index;
                }
            }

            if (myendchar == '+')
            {
                var checkedchar = charlist.Where(p => p.Index < end).ToList();
                var pluschar = checkedchar.Where(p => p.Value == '+').ToList().LastOrDefault();
                var contactchar = checkedchar.Where(p => p.Value == ']').ToList().LastOrDefault();
                if (mynextendchar != '|' && pluschar != null && contactchar != null && pluschar.Index > contactchar.Index)
                {
                    posend = pluschar.Index;
                }
            }
        }

        private string GetshapeOfRemaining(bool parStartind, bool parEndind, List<string> list, int index, int posbegin, int end)
        {
            string returnvalue = "";
            string interuptstring = list[index];
            var ComundList = list.Select((Value, Index) => new { Value, Index })
         .Where(p => ((p.Value.Contains("]") || p.Value.Contains("--(")) && p.Index > index)).ToList().FirstOrDefault();
            if (parStartind == false && parEndind == true && ComundList != null)
            {
                if (ComundList.Value.Length > end && ComundList.Value[end - 1] == '-')
                {
                    returnvalue = "OR";
                }
            }

            return returnvalue;
        }

        private static List<string> GetListByindexforbothRemainAndParrellel(List<int> mylistInex, List<string> list, ref List<string> Remainlist, List<int> myRemainlistInex)
        {
            List<string> mylistvalues = GetListBindex(mylistInex, list);

            Remainlist = GetListBindex(myRemainlistInex, list);

            return mylistvalues;
        }

        private static List<string> GetListBindex(List<int> mylistInex, List<string> list)
        {
            var ComundList = list.Select((Value, Index) => new { Value, Index })
          .Where(p => p.Value.Contains("]") || p.Value.Contains("--(") && p.Value != "" && p.Value != "").ToList();
            List<string> mylistvalues = new List<string>();

            if (mylistInex.Count > 0)
            {
                int index = 0;
                // ComundList.RemoveAt(0);
                foreach (var line in ComundList)
                {
                    if (mylistInex.Contains(line.Index))
                    {
                        if (line.Index > 1)
                            index = line.Index - 1;
                        break;
                    }
                }

                int lastindx = mylistInex.LastOrDefault();
                if (list[lastindx].Contains(">") || list[lastindx].Contains("<") || list[lastindx].Contains("(TD") || list[lastindx].Contains("=="))
                {
                    lastindx = lastindx + 1;
                    if (list.Count < lastindx + 1)
                    {
                        lastindx--;
                    }
                }
                if (ComundList.Count > 0)
                {
                    mylistvalues = list.GetRange(index, (lastindx - index) + 1);
                    //if (list.Count > lastindx)
                    //{
                    //    Remainlist = list.GetRange(lastindx + 1, (list.Count - lastindx) - 1).Where(x => x != "").ToList();
                    //}
                }
            }

            return mylistvalues;
        }

        private static string AddcontacttoLogic(string intiallogic, string contactname)
        {
            if (intiallogic != "")
            {
                intiallogic = intiallogic.Trim();
                if (!intiallogic.EndsWith(","))
                {
                    intiallogic = intiallogic + "," + contactname + ")";
                }
                else
                {
                    intiallogic = intiallogic + contactname + ")";
                }
            }
            else
            {
                intiallogic = contactname;
            }

            return intiallogic;
        }

        private static void ADDLatchinglogic(coil mycoil, Dictionary<string, SafetyTemplate> newSafetyTemplateMap)
        {
            if (mycoil.Coiltype == "TMR" || mycoil.Coiltype == "CTU" || mycoil.Coiltype == "RST" || mycoil.Coiltype == "LCH")
            {
                string modname = mycoil.Name + "_" + mycoil.Coiltype;
                mycoil.Logic = mycoil.Logic.Replace(mycoil.Name + "=", modname + "=");
                mycoil.Name = modname;
            }
        }

        private static void createnewcontactlist(ref List<contact> mycontactlist, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> IDS)
        {
            List<contact> mynewcontactlist = new List<contact>();
            foreach (var con in mycontactlist)
            {
                AddNewContact(con.Name, ref myTemplate, ref IDS, ref mynewcontactlist, con.typeName, "", con.Networknumber);
            }
            mycontactlist = mynewcontactlist;
        }

        private static bool CheckIsParrallelLogic(ref string parreleloutputs, SafetyTemplate myTemplate, string intiallogic, string mycoilname, string curcoiltype)
        {
            bool returnvalue = false;
            List<string> splitedparrallellogic = parreleloutputs.Split('/').Where(x => x != "").ToList();
            string[] stringcoiltypes = new string[] { "LCH", "RST", "", "CTU", "TMR" };
            if (splitedparrallellogic.Count() >= 1)
            {
                string Coiltype = parreleloutputs.Substring(parreleloutputs.IndexOf("["), parreleloutputs.Length - parreleloutputs.IndexOf("["));
                string curlogic = intiallogic.Substring(intiallogic.IndexOf("="), intiallogic.Length - intiallogic.IndexOf("="));
                string parrellelcoil = splitedparrallellogic.Last().Replace(Coiltype, "");
                Coiltype = Coiltype.Replace("[", "").Replace("]", "");
                if (Coiltype != "")
                {
                }
                coil myorginalcoil = myTemplate.SafetycoilMap.LastOrDefault(x => x.Value.Name == parrellelcoil && x.Value.Coiltype == Coiltype &&
                                                x.Value.Logic.Substring(x.Value.Logic.IndexOf("="), x.Value.Logic.Length - x.Value.Logic.IndexOf("="))
                                                    == curlogic).Value;
                if (myorginalcoil != null && curcoiltype == Coiltype || (stringcoiltypes.Contains(Coiltype) && stringcoiltypes.Contains(curcoiltype)))
                {
                    parreleloutputs = mycoilname + "/" + parreleloutputs;
                    returnvalue = true;
                    //  adjustnetworkforparallel(myTemplate, splitedparrallellogic);
                }
                else
                {
                    parreleloutputs = mycoilname + "[" + curcoiltype + "]";
                    myTemplate.Networkcount++;
                }
            }
            return returnvalue;
        }

        private  void SerializeST(List<string> list, ref SafetyTemplate myTemplate, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, List<string> networkDiscption, ref SortedDictionary<int, int> ids, ref List<string> Library, DataTable myDB, string networknumber, List<string> mymismatchList)
        {
            var CompoundList = list.Select((Value, Index) => new { Value, Index })
               .Where(p => p.Value.Contains("+--") && !p.Value.Contains("]") && p.Value.Contains("--+") && p.Value.IndexOf("--+") > p.Value.IndexOf("+--")).ToList();
            List<string> Stlist = new List<string>();
            bool firstcoil = false;
            if ((CompoundList != null) && (CompoundList.Count() != 0) && CompoundList.Count() == 2)
            {
                var thisCompoundFirst = CompoundList.FirstOrDefault();
                var thisCompoundLast = CompoundList.LastOrDefault();
                int upperIndex = thisCompoundLast.Index;

                int lowerIndex = thisCompoundFirst.Index + 1;
                Stlist = list.ToList().GetRange(lowerIndex, (upperIndex - lowerIndex));
                Stlist = RemoveDescrptionSeperators(Stlist);
                SerializeSTCommands(Stlist, networkDiscption, ref myTemplate, ref newSafetyTemplateMap, ref ids, ref Library, myDB, ref firstcoil, networknumber, mymismatchList);
            }
            else
            {
                if ((CompoundList != null) && (CompoundList.Count() != 0) && CompoundList.Count() > 2)
                {
                    string mis = "Network # " + networknumber + ": can't be converted:this network need manual work";
                    if (!mymismatchList.Contains(mis))
                    {
                         mymismatchList.Add(mis);
                    }

                }
            }

            //return Discptionlist;
        }

        private  void SerializeSTCommands(List<string> stlist, List<string> networkDiscption, ref SafetyTemplate myTemplate, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref SortedDictionary<int, int> ids, ref List<string> Library, DataTable myDB, ref bool firstcoilind, string networknumber, List<string> mymismatchList)
        {
            int i = 0;

            foreach (var line in stlist)
            {
                if (myTemplate.Networkcount > MAXNETWORKCOUNTST)//CONTROL NUMBER NETWORKS
                {
                    AddnewTemplate(ref myTemplate, newSafetyTemplateMap, ref ids);
                }
                string description = "";
                var myline = line.Replace("-", "");//negative should consider
                int indexdesstart = myline.ToUpper().IndexOf("{");
                if (indexdesstart != -1)
                {
                    description = myline.Substring(indexdesstart, myline.Length - indexdesstart);
                }
                //if (myline.ToUpper().Replace(" ", "").Contains("IF"))
                if (checkIFexist(myline))
                {
                    List<string> myIFst = GetIFstatement(ref stlist, i);
                    SerializeIF(myIFst, networkDiscption, ref myTemplate, ref ids, ref Library, myDB, networknumber);

                    Updatediscription(networkDiscption, description, ref firstcoilind);
                    myTemplate.Networkcount++;
                    SerializeSTCommands(stlist, networkDiscption, ref myTemplate, ref newSafetyTemplateMap, ref ids, ref Library, myDB, ref firstcoilind, networknumber, mymismatchList);
                    break;
                }
                if (myline.Contains(";"))
                {
                    string[] commanddescrption = line.Split(';').Where(x => x != "" && !x.Contains("{")).ToArray();
                    string command = commanddescrption[0].Replace(" ", "");
                  

                    // string descriptioncommand = commanddescrption[1].Trim();

                    Serializecoilfromcommand(ref myTemplate, command, description, networkDiscption, ref ids, ref firstcoilind, networknumber, mymismatchList,myDB);

                    if (commanddescrption.Count() > 1)
                    {
                        string secondcomand = commanddescrption[1].Replace(" ", "");
                        Serializecoilfromcommand(ref myTemplate, secondcomand, description, networkDiscption, ref ids, ref firstcoilind, networknumber, mymismatchList,myDB);
                    }
                    if (commanddescrption.Count() > 2)
                    {
                        string thirdcomand = commanddescrption[2].Replace(" ", "");
                        Serializecoilfromcommand(ref myTemplate, thirdcomand, description, networkDiscption, ref ids, ref firstcoilind, networknumber, mymismatchList,myDB);
                    }
                }
                if (indexdesstart != -1 && !myline.Contains(".") && !checkIFexist(myline))
                {
                    networkDiscption.Add(description);
                }
                i++;
            }
        }

        private  void AddnewTemplate(ref SafetyTemplate myTemplate, Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref SortedDictionary<int, int> ids)
        {
            string sheetnumber = myTemplate.SheetNumber;
            if (newSafetyTemplateMap.FirstOrDefault(x => x.Key == sheetnumber).Value == null)
            {
                newSafetyTemplateMap.Add(myTemplate.SheetNumber, myTemplate);
            }
            string sheettitle = myTemplate.Template;
            if (sheettitle.Contains(","))
            {
                string network = sheettitle.Split(',').LastOrDefault();
                sheettitle = sheettitle.Substring(0, sheettitle.IndexOf("#")) + network;
            }

            myTemplate = new SafetyTemplate();
            CreateFBDSafetyTemplate(ref myTemplate);
            Addnewsheettemplate(newSafetyTemplateMap, myTemplate.ProgramName, ref ids, ref myTemplate);

            updatesheettitles(sheettitle, ref myTemplate);
        }

        private static bool checkIFexist(string line)
        {
            var myline = line.Replace("-", "");
            int indexIF = myline.ToUpper().IndexOf("IF ");
            int indexdesstart = myline.ToUpper().IndexOf("{");
            int indexdesend = myline.ToUpper().IndexOf("}");
            return (indexdesstart != -1 && indexIF != -1 && indexIF < indexdesstart) || (indexdesstart == -1 && indexIF > -1);
        }

        private static void SerializeIF(List<string> myIFst, List<string> networkDiscption, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, ref List<string> Library, DataTable myDB, string networknumber)
        {
            //  List<string> mycond = GetlogicCondition(myIFst);

            //  List<string> myTruecond = GetlogicTrue(myIFst);

            //  List<string> myfalsecond = GetlogicFalse(myIFst);
            //  ParseIfExperision(mycond, myTruecond, myfalsecond, networkDiscption, ref myTemplate, ref ids);
            Dictionary<string, string> mycoils = new Dictionary<string, string>();
            Dictionary<string, string> mycontacts = new Dictionary<string, string>();
            string newlogic = CreateST_For_IF(myIFst, networkDiscption, ref myTemplate, ref ids, mycoils, mycontacts, ref Library, myDB);
            CreateIFcoilFN(mycoils, mycontacts, ref myTemplate, networkDiscption, Library, ref ids, newlogic, networknumber);
        }

        private static void CreateIFcoilFN(Dictionary<string, string> mycoils, Dictionary<string, string> mycontacts, ref SafetyTemplate myTemplate, List<string> networkDiscption, List<string> library, ref SortedDictionary<int, int> ids, string myinternalogic, string networknumber)
        {
            if (mycoils.Count == 0)
            {
                mycoils.Add("OUT1", "TEMP");
            }
            List<contact> mylist = new List<contact>();

            string coilname = mycoils.FirstOrDefault().Value.Replace("-", "_");
            string coillogic = Getiffn(coilname, myinternalogic, library, mycontacts, mylist, ref ids, ref myTemplate, networknumber);
            //coil mycoil = Addcoil(ref coilname, myTemplate, ref coillogic, ids, "", "", networkDiscption);
            //mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            AddNewCoil(coilname, ref myTemplate, coillogic, ref ids, mylist, "", "", "", networkDiscption, networknumber);
        }

        private static string Getiffn(string coilname, string myinternalogic, List<string> library, Dictionary<string, string> mycontacts, List<contact> mycoillist, ref SortedDictionary<int, int> ids, ref SafetyTemplate myTemplate, string networknumber)
        {
            string mylogic = coilname + "=";
            int myindx = library.IndexOf(myinternalogic) + 1;

            mylogic = mylogic + "STFB" + myindx.ToString() + "(";
            foreach (var con in mycontacts)
            {
                double tagnumber;
                string tag = con.Value.Replace(";", "");
                if (!double.TryParse(tag, out tagnumber))
                {
                    AddNewContact(tag, ref myTemplate, ref ids, ref mycoillist, "", "", networknumber);
                }

                mylogic = mylogic + tag + ",";
            }
            mylogic = mylogic + ")";
            mylogic = mylogic.Replace(",)", ")").Replace(";", "");
            return mylogic;
        }

        private static string CreateST_For_IF(List<string> myIFst, List<string> networkDiscption, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, Dictionary<string, string> mycoils, Dictionary<string, string> mycontacts, ref List<string> Library, DataTable myDB)
        {
            string logic = "";
            // Dictionary<string, string> mycontacts = new Dictionary<string, string>();
            Dictionary<string, string> myFBs = new Dictionary<string, string>();
            string newlogic = "";
            logic = convertIfListTostring(myIFst);
            bool contactind = false;
            bool FBindication = false;

            string[] stringGATES = new string[] { "<=", "<", ">", ">=", "&", "=", "+", "-", "/", "*", "|", "==", "ELSE", "(", "IF", "ABS", "ENDIF", "ENDIF;", "ELSEIF", "THEN", "OVDENABLE", "OVDDISABLE", ";" };
            //string[] stringFBs = new string[] { "OVDENABLE", "OVDDISABLE" };
            string[] stringSEP = new string[] { " " };

            string[] logicLit = logic.Split(stringSEP, StringSplitOptions.None);
            foreach (var mylogicpart in logicLit.Where(x => x != ""))
            {
                string addpart = mylogicpart;
                if (stringGATES.Where(x => x == mylogicpart.ToUpper()).Count() == 0)
                {
                    addpart = Getnewnames(mycoils, mycontacts, contactind, ref FBindication, mylogicpart);
                }
                else
                {
                    Adjustandgetparametertype(ref contactind, ref FBindication, myFBs, mylogicpart, ref addpart);
                }

                newlogic = newlogic + addpart + " ";
            }
            newlogic = ADDdecleration(mycoils, myDB, mycontacts, myFBs) + Environment.NewLine + newlogic;
            newlogic = newlogic.Replace("  ", "").ToUpper().Replace("ENDIF", "END_IF").Replace("ELSEIF", "ELSIF").Replace("&", "AND").Replace("|", "OR").Replace(" = ", "=").Replace(" ; ", ";");
            //  newlogic = "FUNCTION_BLOCK STFB" + (Library.Count() + 1).ToString() + Environment.NewLine + newlogic + Environment.NewLine + "END_FUNCTION_BLOCK";
            newlogic = "FUNCTION_BLOCK STFB" + Environment.NewLine + newlogic + Environment.NewLine + "END_FUNCTION_BLOCK";
            if (Library.Exists(x => x == newlogic))
            {
            }
            else
            {
                Library.Add(newlogic);
            }
            return newlogic;
        }

        private static string ADDdecleration(Dictionary<string, string> mycoils, DataTable myDB, Dictionary<string, string> mycontacts, Dictionary<string, string> myFBS)
        {
            string mydec = ADDdeclerationforccontactsORCoil(mycontacts, myDB, "VAR_INPUT");
            mydec = mydec + Environment.NewLine + ADDdeclerationforccontactsORCoil(myFBS, myDB, "VAR");

            mydec = mydec + Environment.NewLine + ADDdeclerationforccontactsORCoil(mycoils, myDB, "VAR_OUTPUT");

            return mydec;
        }

        private static void ADDdeclerationforcoils(Dictionary<string, string> mycoils, DataTable myDB)
        {
            //throw new NotImplementedException();
        }

        private static string ADDdeclerationforccontactsORCoil(Dictionary<string, string> mycontacts, DataTable myDB, string dectype)
        {
            string[] stringFBs = new string[] { "OVDENABLE", "OVDDISABLE" };
            string mdec = "";
            string addedtype = "";
            addedtype = Gettypeword(dectype);
            string enddeclaration = "";
            if (mycontacts.Count() != 0 || addedtype != "")
            {
                mdec = dectype;
                enddeclaration = Environment.NewLine + "END_VAR";
            }

            bool addindication = true;
            foreach (var con in mycontacts)
            {
                string tag = con.Value.Trim().Replace(";", "").Replace("-", "_");
                string mytype = GetDBType(myDB, tag);
                string modType = "";
                Gettype(stringFBs, con, tag, mytype, ref modType);
                if (modType.Contains("BOOL"))
                {
                    addindication = false;
                }
                if (mytype == null)
                {
                }
                mdec = mdec + Environment.NewLine + con.Key.Replace(";", "") + ":" + modType;
            }
            if (addindication && addedtype != "")
            {
                addedtype = addedtype + (mycontacts.Count() + 1);
                mdec = mdec + Environment.NewLine + addedtype + ":BOOL ;";
            }
            mdec = mdec + enddeclaration;

            return mdec;
        }

        private static string GetDBType(DataTable myDB, string tag)
        {
            return (from DataRow dr in myDB.Rows
                    where (string)dr["TagName"].ToString().ToUpper() == tag.ToUpper()
                    select (string)dr["DataType"]).FirstOrDefault();
        }

        private static string Gettypeword(string dectype)
        {
            string addedtype = "";
            switch (dectype)
            {
                case "VAR_INPUT":
                    addedtype = "IN";
                    break;

                case "VAR_OUTPUT":
                    addedtype = "OUT";
                    break;
            }

            return addedtype;
        }

        private static void Gettype(string[] stringFBs, KeyValuePair<string, string> con, string tag, string mytype, ref string modType)
        {
            if (!string.IsNullOrEmpty(mytype))
            {
                modType = GetTristationType(mytype, modType);
            }
            else
            {
                if (stringFBs.Where(x => x == tag.ToUpper()).Count() != 0)
                {
                    modType = tag + " ;";
                }

                double intvalue;
                string value = con.Value.Replace(" ", "").Replace(";", "");
                intvalue = GetTypeBasedOnValue(ref modType, value);

                
            }
        }

        private static double GetTypeBasedOnValue(ref string modType, string value)
        {
            double intvalue;
            if (double.TryParse(value, out intvalue))
            {
                if (value.Contains("."))
                {
                    modType = "REAL ;";
                }
                else
                {
                    modType = "DINT ;";
                }
            }
            if (value == "1" || value == "0")
            {
                modType = "BOOL ;";
            }

            return intvalue;
        }

        private static string GetTristationType(string mytype, string modType)
        {

            switch (mytype)
            {
                case "D":
                    modType = "BOOL ;";
                    break;

                case "R":
                    modType = "REAL ;";
                    break;

                case "I":
                    modType = "DINT ;";
                    break;
            }

            return modType;
        }

        private static void Adjustandgetparametertype(ref bool contactind, ref bool FBindication, Dictionary<string, string> myFBs, string mylogicpart, ref string addpart)
        {
            string[] contactindgates = new string[] { "<=", "<", ">", ">=", "&", "=", "+", "-", "/", "*", "|", "==", "IF", "ABS", "ENDIF;", "ELSEIF", "OVDENABLE", "OVDDISABLE" };
            string[] stringFBs = new string[] { "OVDENABLE", "OVDDISABLE" };
            if (addpart == "=" && !contactind)
            {
                addpart = addpart.Replace("=", ":=");
            }
            if ((contactindgates.Where(x => x == mylogicpart.ToUpper()).Count() != 0))
            {
                contactind = true;
            }
            else
            {
                contactind = false;
            }
            if ((stringFBs.Where(x => x == mylogicpart.ToUpper()).Count() != 0))
            {
                FBindication = true;
                string myfb = "FB_" + mylogicpart + (myFBs.Count() + 1).ToString();
                myFBs.Add(myfb, mylogicpart);
                addpart = AdjustFB(addpart, myfb);
            }

            addpart = addpart.Trim();
        }

        private static string AdjustFB(string addpart, string myfb)
        {
            // throw new NotImplementedException();

            if (addpart.ToUpper().Contains("OVDENABLE") || addpart.ToUpper().Contains("OVDDISABLE"))
            {
                myfb = myfb + "(CI:=true, CHASSIS:=";
            }
            return myfb;
        }

        private static string convertIfListTostring(List<string> myIFst)
        {
            string logic = "";
            foreach (var line in myIFst)
            {
                var myline = line;

                if (line.ToUpper().Contains("-IF"))
                {
                    int indexif = myline.ToUpper().IndexOf("-IF") + 1;
                    myline = myline.Substring(indexif, myline.Length - indexif);
                }
                if (line.ToUpper().Contains("- IF"))
                {
                    int indexif = myline.ToUpper().IndexOf("- IF") + 2;
                    myline = myline.Substring(indexif, myline.Length - indexif);
                }

                if (line.ToUpper().Contains("{") && line.ToUpper().Contains("}"))
                {
                    int indexif = myline.ToUpper().IndexOf("{");
                    myline = myline.Substring(0, indexif);
                }
                myline = myline.Replace("==", "=");
                myline = myline.Replace("(", " (");
                myline = myline.Replace("=", " = ");
                myline = myline.Replace("<", " < ");
                myline = myline.Replace(">", " > ");
                myline = myline.Replace("  ", " ");
                myline = myline.Replace(";", " ; ");
                logic = logic + myline + " " + Environment.NewLine + " ";
            }

            return logic;
        }

        private static string Getnewnames(Dictionary<string, string> mycoils, Dictionary<string, string> mycontacts, bool contactind, ref bool fBindication, string mylogicpart)
        {
            string addpart = mylogicpart;
            string tag = mylogicpart.Replace("(", "").Replace(")", "");
            if (fBindication && mylogicpart.Contains("$R0"))
            {
                string chassis = mylogicpart.Substring(2, 2);
                string addpart1 = Addinput(mycontacts, chassis + ",", chassis);

                string slot = mylogicpart.Substring(4, 2);
                string addpart2 = Addinput(mycontacts, " SLOT:=" + slot, slot);
                addpart = addpart1 + addpart2 + ");";
            }
            else
            {
                if (contactind)
                {
                    addpart = Addinput(mycontacts, addpart, tag);
                }
                else
                {
                    addpart = Addoutput(mycoils, addpart, tag);
                }
            }

            if (mylogicpart.Contains(";"))
            {
                fBindication = false;
            }

            return addpart;
        }

        private static string Addoutput(Dictionary<string, string> mycoils, string addpart, string tag)
        {
            var mytag = mycoils.FirstOrDefault(x => x.Value == tag);
            if (mytag.Value == null && tag != Environment.NewLine)
            {
                int coilcount = mycoils.Count() + 1;
                mycoils.Add("OUT" + coilcount.ToString(), tag);
                addpart = addpart.Replace(tag, "OUT" + coilcount.ToString());
            }
            else
            {
                if (tag != Environment.NewLine)
                {
                    addpart = addpart.Replace(tag, mytag.Key);
                }
            }

            return addpart;
        }

        private static string Addinput(Dictionary<string, string> mycontacts, string addpart, string tag)
        {
            var mytag = mycontacts.FirstOrDefault(x => x.Value == tag);
            if (mytag.Value == null && tag != Environment.NewLine)
            {
                // contact mycon = CreateContact(tag, myTemplate, ids, ' ');

                int contactcount = mycontacts.Count() + 1;
                string newinput = "IN" + contactcount.ToString();
                if (tag.EndsWith(";"))
                {
                    newinput = newinput + ";";
                }

                mycontacts.Add(newinput, tag);
                addpart = addpart.Replace(tag, newinput);
            }
            else
            {
                if (tag != Environment.NewLine)
                {
                    addpart = addpart.Replace(tag, mytag.Key);
                }
            }

            return addpart;
        }

        private static void ParseIfExperision(List<string> mycond, List<string> myTruecond, List<string> myfalsecond, List<string> networkDiscption, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids)
        {
            string condition = "";
            foreach (var line in mycond)
            {
                var myline = line.Trim();
                int indexif = myline.ToUpper().IndexOf("IF") + 2;
                myline = myline.Substring(indexif, myline.Length - indexif);
                condition = condition + myline;
                if (condition.ToUpper().Contains("THEN"))
                {
                    int indexTHEN = myline.ToUpper().IndexOf("THEN");
                    condition = condition.Substring(0, indexTHEN).Trim();
                    break;
                }
            }

            //if (condition.Contains("&"))
            //{
            //    condition = ParseAnd(condition, "AND");
            //}

            //if (condition.Contains("|"))
            //{
            //    condition = ParseAnd(condition, "OR");
            //}
            if (condition.Contains("<") || condition.Contains(">") || condition.Contains("&"))
            {
                condition = ParseGATES(condition);
            }

            List<string> mytruest = Extraxtaction(myTruecond);
            List<string> myfalsest = Extraxtaction(myfalsecond);
        }

        private static List<string> Extraxtaction(List<string> myTruecond)
        {
            List<string> myreturn = new List<string>();
            if (myTruecond.Count() == 1)
            {
                string[] stringcoils = new string[] { ";" };
                string[] stringSEP = new string[] { " " };

                string[] logicLit = myTruecond[0].Split(stringcoils, StringSplitOptions.None);
                foreach (var coil in logicLit.Where(x => x != ""))
                {
                    string truest = ParseGATES(coil);
                    if (truest == "")
                    {
                        truest = coil;
                    }
                    myreturn.Add(truest);
                }
            }
            return myreturn;
        }

        private static string ParseGATES(string condition)
        {
            string[] stringGATES = new string[] { "<=", "<", ">", ">=", "&" };
            string[] stringSEP = new string[] { " " };

            string[] logicLit = condition.Split(stringSEP, StringSplitOptions.None);
            List<string> output = new List<string>();
            Stack<string> Stack = new Stack<string>();
            string logical = "";
            string command = "";
            string totalcommand = "";
            foreach (var line in logicLit)
            {
                foreach (char mychar in line)
                {
                    string mystring = mychar.ToString();
                    if (stringGATES.Where(x => x == mystring).Count() != 0)
                    {
                        int posgate = Getpostiongate(totalcommand);
                        while (Stack.Count > 0)
                        {
                            string popup = Stack.Pop();
                            // output.Add(popup);
                            InsertTextInpos(ref totalcommand, posgate, popup);
                            // totalcommand = popup + totalcommand;
                        }
                        //semi , adjust here
                        foreach (var iline in output)
                        {
                            totalcommand = totalcommand + iline + ",";
                        }
                        output.Clear();
                        Stack.Push(line);
                        command = "";
                        break;
                    }
                    else
                    {
                        string popup2 = "";
                        switch (mystring)
                        {
                            //case "(":
                            //  //  Stack.Push(mystring);
                            // //   command = "";
                            //    totalcommand = totalcommand+"(";

                            //  break;
                            case ")":
                                while (Stack.Count > 0)
                                {
                                    //output.Add(Stack.Pop());
                                    int posgate = Getpostiongate(totalcommand);
                                    popup2 = Stack.Pop();
                                    InsertTextInpos(ref totalcommand, posgate, popup2);
                                    //totalcommand = popup2 + totalcommand;
                                    // output.Add(popup);
                                }

                                output.Add(command);
                                foreach (var iline in output)
                                {
                                    totalcommand = totalcommand + iline + ",";
                                }
                                totalcommand = totalcommand + ")";
                                totalcommand = totalcommand.Replace(",)", ")");
                                output.Clear();
                                command = "";
                                break;

                            default:
                                command = command + mystring;
                                break;
                        }

                        //  output.Add(line);
                    }
                }
                if (command != "")
                    output.Add(command);
            }

            return totalcommand;
        }

        private static void InsertTextInpos(ref string totalcommand, int posgate, string popup)
        {
            if (totalcommand.Length > posgate)
            {
                string start = totalcommand.Substring(0, posgate);
                string sep = "";
                if (start.Length > 0)
                {
                    sep = ",";
                }

                string reset = totalcommand.Substring(posgate, totalcommand.Length - posgate);
                totalcommand = start + sep + popup + reset;
            }
        }

        private static int Getpostiongate(string logic)
        {
            int track = 0;
            int pos = logic.Length;
            foreach (char mychar in logic.Reverse())
            {
                if (mychar == ')')
                {
                    track--;
                }

                if (mychar == '(')
                {
                    track++;
                }
                pos--;
                if (track == 1)
                {
                    break;
                }
            }
            return pos;
        }

        private static string ParseAnd(string condition, string gate)
        {
            string returnvalue = gate + "(";
            string[] stringSeparators = new string[] { "&" };
            string[] conttactsNamesLit = condition.Split(stringSeparators, StringSplitOptions.None);
            foreach (var logic in conttactsNamesLit)
            {
                returnvalue = returnvalue + logic.Trim() + ",";
            }
            return returnvalue = (returnvalue + ")").Replace(",)", ")");
        }

        private static List<string> GetlogicFalse(List<string> myIFst)
        {
            List<string> returnlist = new List<string>();
            var Elsest = myIFst.Select((Value, Index) => new { Value, Index })
           .Where(p => p.Value.Contains("ELSE")).ToList();
            if (Elsest.Count() != 0)
            {
                int lowerIndex = Elsest.FirstOrDefault().Index;

                var ENDIF = myIFst.Select((Value, Index) => new { Value, Index })
         .Where(p => p.Value.Contains("ENDIF")).ToList();
                int upperrIndex = ENDIF.FirstOrDefault().Index;

                int len = lowerIndex;
                if (upperrIndex == lowerIndex)
                {
                    len = 1;
                }
                else
                {
                    len = (upperrIndex - lowerIndex) - 1;
                }
                returnlist = myIFst.ToList().GetRange(lowerIndex + 1, len);
            }

            return returnlist.ToList();
        }

        private static List<string> GetlogicTrue(List<string> myIFst)
        {
            List<string> returnlist = new List<string>();
            var Thenst = myIFst.Select((Value, Index) => new { Value, Index })
           .Where(p => p.Value.Contains("THEN")).ToList();
            int lowerIndex = Thenst.FirstOrDefault().Index;

            var Truest = myIFst.Select((Value, Index) => new { Value, Index })
     .Where(p => p.Value.ToUpper().Contains("ELSE") || p.Value.ToUpper().Contains("ENDIF")).ToList();
            int upperrIndex = Truest.FirstOrDefault().Index;

            int len = lowerIndex;
            if (upperrIndex == lowerIndex || upperrIndex - 1 == lowerIndex)
            {
                len = 1;
            }
            else
            {
                len = (upperrIndex - lowerIndex) - 1;
            }
            returnlist = myIFst.ToList().GetRange(lowerIndex + 1, len);
            return returnlist.ToList();
        }

        private static List<string> GetlogicCondition(List<string> myIFst)
        {
            //string[] stringSeparators = new string[] { "THEN" };
            List<string> returnlist = new List<string>();
            var Thenst = myIFst.Select((Value, Index) => new { Value, Index })
           .Where(p => p.Value.Contains("THEN")).ToList();
            int lowerIndex = Thenst.FirstOrDefault().Index;
            int len = lowerIndex;
            if (lowerIndex == 0)
            {
                len = 1;
            }
            returnlist = myIFst.ToList().GetRange(0, len);
            return returnlist.ToList();
        }

        private static List<string> GetIFstatement(ref List<string> stlist, int start)
        {
            var CompoundList = stlist.Select((Value, Index) => new { Value, Index })
              .Where(p => p.Value.ToUpper().Contains("ENDIF")).ToList();
            List<string> returntlist = new List<string>();
            if ((CompoundList != null) && (CompoundList.Count() != 0))
            {
                // var Endifindex = CompoundList.LastOrDefault().Index;
                var Endifindex = CompoundList.FirstOrDefault().Index;
                var EndifInternalindex = CompoundList.LastOrDefault().Index;
                if(CheckNestedIf(stlist, start))
                {
                    Endifindex = EndifInternalindex;
                }
                var lastindex = stlist.Count();

                returntlist = stlist.ToList().GetRange(start, (Endifindex - start) + 1);
                stlist = stlist.ToList().GetRange(Endifindex + 1, (lastindex - (Endifindex + 1))).Where(x => x != "").ToList();
            }

            return returntlist;
        }

        private static bool CheckNestedIf(List<string> stlist, int start)
        {
            bool returnValue = false;
            int indexIf = 0;
            int indexEndif = 0;
            foreach(string line in stlist)
            {
                if(checkIFexist(line))
                    indexIf= stlist.IndexOf(line);
                if(line.Replace(" ","").ToUpper().Contains("ENDIF;"))
                {
                    indexEndif = stlist.IndexOf(line);
                    break;
                }
            }
            if(indexEndif!=0 && indexIf>start)
            {
                //NESMA MAY MORE CHECK
                returnValue = true;
            }
            return returnValue;
        }

        private static void Serializecoilfromcommand(ref SafetyTemplate myTemplate, string command, string description, List<string> networkDiscption, ref SortedDictionary<int, int> ids, ref bool firstcoilind, string networknumber, List<string> myListMismatch, DataTable myDB)
        {
            if (command.ToUpper().Contains("=AIN("))
            {
                string coilname = command.Split('=')[0].Replace("-", "_");
                string fn = command.Split('=')[1];
                Updatediscription(networkDiscption, description, ref firstcoilind);
                string logic = RearangeAINfn(fn, coilname, ref myTemplate, ref ids, networkDiscption, networknumber, description);
                myTemplate.Networkcount++;
                return;//need update for exit
            }
            if (command.ToUpper().Contains("AND(")|| command.ToUpper().Contains("OR("))
            {
                string coilname = command.Split('=')[0].Replace("-", ""); ;
                string fn = command.Split('=')[1];
                Updatediscription(networkDiscption, description, ref firstcoilind);
                string logic = RearangeANDfn2(fn, coilname, ref myTemplate, ref ids, networkDiscption, networknumber);
                myTemplate.Networkcount++;
                return;//need update for exit
            }

            if (command.ToUpper().Contains("=SCALE("))
            {
                string coilname = command.Split('=')[0].Replace("-", ""); ;
                string fn = command.Split('=')[1];
                Updatediscription(networkDiscption, description, ref firstcoilind);
                string logic = RearangeScalefn(fn, coilname, ref myTemplate, ref ids, networkDiscption, networknumber);
                myTemplate.Networkcount++;
                return;//need update for exit
            }
            if (command.Replace(" ", "").ToUpper().Contains("=MEDSEL("))
            {
                string coilname = command.Split('=')[0].Replace("-", ""); ;
                string fn = command.Split('=')[1];
                Updatediscription(networkDiscption, description, ref firstcoilind);
                string logic = RearangeMedSelfn(fn, coilname, ref myTemplate, ref ids, networkDiscption, networknumber);
                myTemplate.Networkcount++;
                return;//need update for exit
            }
            if (command.Replace(" ", "").ToUpper().Contains("=PERDEV("))
            {
                string coilname = command.Split('=')[0].Replace("-", "_");
                string fn = command.Split('=')[1];

                Updatediscription(networkDiscption, description, ref firstcoilind);
                string logic = RearangePERDEVfn(fn, coilname, ref myTemplate, ref ids, networkDiscption, networknumber);
                myTemplate.Networkcount++;
                return;//need update for exit
            }
            if (command.Replace(" ", "").ToUpper().Contains("=TCJCONV("))
            {
                string coilname = command.Split('=')[0].Replace("-", ""); ;
                string fn = command.Split('=')[1];
                Updatediscription(networkDiscption, description, ref firstcoilind);
                string logic = RearangeTcJConvfn(fn, coilname, ref myTemplate, ref ids, networkDiscption, networknumber);
                myTemplate.Networkcount++;
                myTemplate.Networkcount++;
                return;//need update for exit
            }
            if (CheckMove(command))
            {
                string coilname = command.Split('=')[0].Replace("-", "_");
                //if(coilname.Contains("TTconvM"))
                //{
                //}
                string fn = command.Split('=')[1].Replace("-", "_");
                Updatediscription(networkDiscption, description, ref firstcoilind);
                string logic = RearangeMovefn(fn, coilname, ref myTemplate, ref ids, networkDiscption, networknumber, description,myDB);
                myTemplate.Networkcount++;
            }
            else
            {
                //add code for cant be converted
                myListMismatch.Add("Network # " + networknumber + ": can't be converted:this network need manual work command: " + command + " not exist in the tool library");
            }
        }

        private static string RearangeANDfn(string fn, string coilname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, List<string> networkDiscption, string networknumber)
        {
            string returnvalue = fn.Replace("-", "_"); ;
            string tag1 = fn.Substring(fn.IndexOf("(") + 1, (fn.IndexOf(",") - fn.IndexOf("(")) - 1).Replace("-", "_").Replace("OR", "").Replace("AND", "").Replace("(", "").Replace(")", "");
            string tag2 = fn.Split(',')[1].Replace("-", "_").Replace("OR", "").Replace("AND", "").Replace("(", "").Replace(")", "");
            string tag3 = fn.Split(',').LastOrDefault().Replace(")", "").Replace("-", "_").Replace("OR","").Replace("AND", "").Replace("(", "").Replace(")", "");

            //returnvalue = "AIN(" + tag3 + "," + tag1 + "," + tag2 + ")";
            returnvalue = coilname + "=" + fn.Replace("-", "_");
            contact mycon = AddContact(tag1, myTemplate, ids, tag1.Contains("!")?"/":"");
            contact mycon2 = AddContact(tag2, myTemplate, ids, tag2.Contains("!") ? "/" : "");
            contact mycon3 = AddContact(tag3, myTemplate, ids, tag3.Contains("!") ? "/" : "");
            coil mycoil = Addcoil(ref coilname, myTemplate, ref returnvalue, ids, "", "", networkDiscption, networknumber);
            mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            mycoil.MyLogic.LogiccontactMap.Add(mycon2.localId, mycon2);
            mycoil.MyLogic.LogiccontactMap.Add(mycon3.localId, mycon3);
            return returnvalue;
        }

        private static string RearangeANDfn2(string fn, string coilname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, List<string> networkDiscption, string networknumber)
        {
            string returnvalue = fn.Replace("-", "_"); ;
            returnvalue = coilname + "=" + fn.Replace("-", "_");
            coil mycoil = Addcoil(ref coilname, myTemplate, ref returnvalue, ids, "", "", networkDiscption, networknumber);
            var tags = fn.Split(',');
            foreach(var tag in tags)
            {
                string tagmod = tag.Replace(")", "").Replace("-", "_").Replace("OR", "").Replace("AND", "").Replace("(", "").Replace(")", "").Replace(" ","");
                contact mycon = AddContact(tagmod, myTemplate, ids, tagmod.Contains("!") ? "/" : "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            }

          
            //returnvalue = "AIN(" + tag3 + "," + tag1 + "," + tag2 + ")";
            
           
            return returnvalue;
        }

        private static string RearangeTcJConvfn(string fn, string coilname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, List<string> networkDiscption, string networknumber)
        {
            string returnvalue = fn;
            string tag1 = fn.Substring(fn.IndexOf("(") + 1, (fn.IndexOf(",") - fn.IndexOf("(")) - 1).Replace("-", "_");
            string tag2 = fn.Split(',')[1].Replace("-", "_"); ;
            string tag3 = fn.Split(',')[2].Replace("-", "_"); ;
            string tag4 = fn.Split(',').LastOrDefault().Replace(")", "").Replace("-", "_"); ;

            //returnvalue = "AIN(" + tag3 + "," + tag1 + "," + tag2 + ")";
            returnvalue = coilname + "=" + "TCJ_CONV(" + tag1 + "," + tag2 + "," + tag3 + "," + tag4 + ")";
            contact mycon = null;
            contact mycon2 = null;
            contact mycon3 = null;
            contact mycon4 = null;
            coil mycoil = Addcoil(ref coilname, myTemplate, ref returnvalue, ids, "", "", networkDiscption, networknumber);
            if (!CheckHexnumber(ref tag1))
            {
                mycon = AddContact(tag1, myTemplate, ids, "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            }
            if (!CheckHexnumber(ref tag2))
            {
                mycon2 = AddContact(tag2, myTemplate, ids, "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon2.localId, mycon2);
            }
            if (!CheckHexnumber(ref tag3))
            {
                mycon3 = AddContact(tag3, myTemplate, ids, "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon3.localId, mycon3);
            }
            if (!CheckHexnumber(ref tag4))
            {
                mycon4 = AddContact(tag4, myTemplate, ids, "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon4.localId, mycon4);
            }

            return returnvalue;
        }

        private static string RearangePERDEVfn(string fn, string coilname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, List<string> networkDiscption, string networknumber)
        {
            string returnvalue = fn.Replace("-", "_");
            string tag1 = fn.Substring(fn.IndexOf("(") + 1, (fn.IndexOf(",") - fn.IndexOf("(")) - 1).Replace("-", "_");
            string tag2 = fn.Split(',')[1].Replace("-", "_");
            string tag3 = fn.Split(',')[2].Replace("-", "_");
            string tag4 = fn.Split(',').LastOrDefault().Replace(")", "").Replace("-", "_");

            //returnvalue = "AIN(" + tag3 + "," + tag1 + "," + tag2 + ")";
            returnvalue = coilname + "=" + fn.Replace("-", "_");
            contact mycon = AddContact(tag1, myTemplate, ids, "");
            contact mycon2 = AddContact(tag2, myTemplate, ids, "");
            double oudble = 0.0;
       
            
            
            coil mycoil = Addcoil(ref coilname, myTemplate, ref returnvalue, ids, "", "", networkDiscption, networknumber);

            mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            mycoil.MyLogic.LogiccontactMap.Add(mycon2.localId, mycon2);
            if (!double.TryParse(tag3, out oudble))
            {
                contact mycon3 = AddContact(tag3, myTemplate, ids, "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon3.localId, mycon3);
            }

            if (!double.TryParse(tag4, out oudble))
            {
                contact mycon4 = AddContact(tag4, myTemplate, ids, "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon4.localId, mycon4);
            }

            
            return returnvalue;
        }

        private static string RearangeMedSelfn(string fn, string coilname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, List<string> networkDiscption, string networknumber)
        {
            string returnvalue = fn.Replace("-", "_"); ;
            string tag1 = fn.Substring(fn.IndexOf("(") + 1, (fn.IndexOf(",") - fn.IndexOf("(")) - 1).Replace("-", "_");
            string tag2 = fn.Split(',')[1].Replace("-", "_");
            string tag3 = fn.Split(',').LastOrDefault().Replace(")", "").Replace("-", "_");

            //returnvalue = "AIN(" + tag3 + "," + tag1 + "," + tag2 + ")";
            returnvalue = coilname + "=" + fn.Replace("-", "_");
            contact mycon = AddContact(tag1, myTemplate, ids, "");
            contact mycon2 = AddContact(tag2, myTemplate, ids, "");
            contact mycon3 = AddContact(tag3, myTemplate, ids, "");
            coil mycoil = Addcoil(ref coilname, myTemplate, ref returnvalue, ids, "", "", networkDiscption, networknumber);
            mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            mycoil.MyLogic.LogiccontactMap.Add(mycon2.localId, mycon2);
            mycoil.MyLogic.LogiccontactMap.Add(mycon3.localId, mycon3);
            return returnvalue;
        }

        private static void Updatediscription(List<string> networkDiscption, string description, ref bool firstcoilind)
        {
            if (!firstcoilind)
            {
                if (description != "")
                {
                    networkDiscption.Add(description);
                }

                firstcoilind = true;
            }
            else
            {
                networkDiscption.Clear();
                if (description != "")
                    networkDiscption.Add(description);
            }
        }
        //st FOR SETPOINTS(CheckHexnumber)
        private static string RearangeMovefn(string fn, string coilname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, List<string> networkDiscption, string networknumber,string discription, DataTable myDB)
        {
            string tag1 = fn.Replace("$", "SYS").Replace("-", "_"); ;
            string returnvalue = tag1;
            returnvalue = coilname + "=" + tag1;
            bool isNumber = CheckHexnumber(ref tag1);
            coil mycoil = new coil();
            if (!STConstantProgram|| !isNumber)
            {
                 mycoil = Addcoil(ref coilname, myTemplate, ref returnvalue, ids, "", "", networkDiscption, networknumber);
            }
            

          

            if (isNumber)
            {
                if(STConstantProgram)
                {
                    string dbType = GetDBType(myDB, coilname);
                    string moType = GetTristationType(dbType, "");
                    if (string.IsNullOrEmpty(moType)) 
                    { GetTypeBasedOnValue(ref moType, tag1); }
                    //AddConstantToStProgram(coilname, tag1, discription, networknumber, "", moType.Replace(" ;",";"));
                    if((coilname.ToUpper().Contains("ALM")|| coilname.ToUpper().Contains("TRP"))&& !discription.Contains("TIME"))
                    {
                        discription = "";
                    }
                    AddConstantToStProgram(coilname, tag1, discription, networknumber, "", moType.Replace(" ;", ";"));
                }
                else
                {
                    returnvalue = coilname + "=" + tag1;
                    mycoil.Logic = returnvalue;
                }
                
            }
            else
            {
                contact mycon = AddContact(tag1, myTemplate, ids, "");

                mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            }

            return returnvalue;
        }

        private static bool CheckMove(string command)
        {
            bool returnvalue = false;
            string[] stringGATES = new string[] { "<=", "<", ">", ">=", "&", "=", "+", "-", "/", "*", "|", "==", "ELSE", "(", "IF", "ABS", "ENDIF;", "ELSEIF", "THEN", "OVDENABLE", "OVDDISABLE" };
            string[] stringFBs = new string[] { "OVDENABLE", "OVDDISABLE" };
            string[] stringSEP = new string[] { " " };
            command = command.Replace(" ", "");
            if (command.Contains("=-"))
            {
                command = command.Replace("=-", "=");
            }

            string[] logicLit = command.Split(stringGATES, StringSplitOptions.None);

            if (logicLit.Count() == 2 && command.Contains("="))
            {
                returnvalue = true;
            }

            return returnvalue;
        }

        private static string RearangeScalefn(string fn, string coilname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, List<string> networkDiscption, string networknumber)
        {
            string returnvalue = fn.Replace("-", "_");
            string tag1 = fn.Substring(fn.IndexOf("(") + 1, (fn.IndexOf(",") - fn.IndexOf("(")) - 1).Replace("-", "_");
            string tag2 = fn.Split(',')[1].Replace("-", "_");
            string tag3 = fn.Split(',').LastOrDefault().Replace(")", "").Replace("-", "_");

            //returnvalue = "AIN(" + tag3 + "," + tag1 + "," + tag2 + ")";
            returnvalue = coilname + "=" + fn.Replace("-", "_");
            contact mycon = AddContact(tag1, myTemplate, ids, "");
            coil mycoil = Addcoil(ref coilname, myTemplate, ref returnvalue, ids, "", "", networkDiscption, networknumber);
            mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            return returnvalue;
        }

        private static string RearangeAINfn(string fn, string coilname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> ids, List<string> networkDiscption, string networknumber,string discription)
        {
            string returnvalue = fn;
            string tag1 = fn.Substring(fn.IndexOf("(") + 1, (fn.IndexOf(",") - fn.IndexOf("(")) - 1).Replace("-", "_"); ;
            string tag2 = fn.Split(',')[1];
            string tag3 = fn.Split(',').LastOrDefault().Replace(")", "");

            double oudble = 0.0;

            if (!double.TryParse(tag2, out oudble))
            {
                tag2 = tag2.Replace("-", "_");
            }

            if (!double.TryParse(tag3, out oudble))
            {
                tag3 = tag3.Replace("-", "_");
            }

            returnvalue = "AIN(" + tag2 + "," + tag1 + "," + tag3 + ")";
            returnvalue = coilname + "=" + returnvalue;
            contact mycon = AddContact(tag1, myTemplate, ids, "");
            coil mycoil = Addcoil(ref coilname, myTemplate, ref returnvalue, ids, "", "", networkDiscption, networknumber);
            if (!double.TryParse(tag2, out oudble))
            {
                contact mycon1 = AddContact(tag2, myTemplate, ids, "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon1.localId, mycon1);
            }
            else
            {
                if(STConstantProgram)
                {
                    AddConstantToStProgram(tag1, tag2, discription, networknumber, "_HIEU","REAL;");
                }
            }

            if (!double.TryParse(tag3, out oudble))
            {
                contact mycon1 = AddContact(tag3, myTemplate, ids, "");
                mycoil.MyLogic.LogiccontactMap.Add(mycon1.localId, mycon1);
            }
            else
            {
                if (STConstantProgram)
                {
                    AddConstantToStProgram(tag1, tag3,"", "", "_LOEU","REAL;");
                }
            }
            mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            return returnvalue;
        }

        private static void AddConstantToStProgram(string tag1, string constant, string networkDiscption, string networknumber, string suffix,string tSType)
        {
            networkDiscption = networkDiscption.Replace("{", "").Replace("}", "");
           if (mySTprogram==null)
            {
                mySTprogram = new List<string>();
                mySTConfiguration= new List<string>();
            }
            bool newnetwork = !string.IsNullOrEmpty(networknumber)&&IsmySTprogramContainThisNetwork(@"(* Network " + networknumber + @"*)");
            if (newnetwork) { mySTprogram.Add(""); }
            //foreach (string Ddescription in networkDiscption)
            // {
            if (!string.IsNullOrEmpty(networkDiscption)) { mySTprogram.Add(@"(*" + networkDiscption + @"*)"); };
             //}

             if (newnetwork) { mySTprogram.Add(@"(* Network " + networknumber + @"*)"); }
             mySTprogram.Add(AdjustLenthofST(tag1 + suffix)+":= "+ constant+";");
            //mySTConfiguration.Add(tag1 + suffix + @"            : " + tSType);
            mySTConfiguration.Add(AdjustLenthofST(tag1 + suffix)+": " + tSType);

        }

        private static bool IsmySTprogramContainThisNetwork(string networkdesription)
        {
            bool returnVal = false;
            returnVal=string.IsNullOrEmpty( mySTprogram.FirstOrDefault(x => x.Contains(networkdesription)));
            return returnVal;


        }

        private static string AdjustLenthofST(string tag)
        {
            string txt = tag;
            for(int i= tag.Length;; i++)
            {
                if (txt.Length > 23)
                {
                    break;
                }
                 txt = txt + " ";
               
            }
            return txt;
        }

        private static string CheckORwoneInputinlastlogic(string intiallogic)
        {
            string[] stringSeparators = new string[] { "," };
            string outputremainlogic, lastinputs, lastlogic = ""; int inputpin = 0;
            string returnvalue = intiallogic;
            intiallogic = intiallogic.Trim();
            Analaizelogicstatement(intiallogic, ref lastlogic, out outputremainlogic, out lastinputs, out inputpin);
            string[] conttactsNamesLit = lastinputs.Split(stringSeparators, StringSplitOptions.None);
            if (conttactsNamesLit.Count() == 1 && outputremainlogic != "")
            {
                // returnvalue = outputremainlogic + conttactsNamesLit[0] + ",";
            }
            else
            {
                if (conttactsNamesLit.Count() == 1 && outputremainlogic == "")
                {
                    returnvalue = conttactsNamesLit[0];
                }
            }
            return returnvalue;
        }

        private static void SetOringlogic(ref string OrLogic, ref string intiallogic, string contactname)
        {
            if (contactname != "")
            {
                if (OrLogic != "")
                {
                    //intiallogic = intiallogic.Replace(OrLogic, "");
                    //OrLogic = OrLogic + "," + contactname + "),";
                    //OrLogic = OrLogic.Replace(",,", ",");
                    UpdateLogicWcompletepath(ref OrLogic, ref intiallogic, contactname);
                }

                if (intiallogic != "" || OrLogic != "")
                {
                    //intiallogic = intiallogic + ",OR(" + contactname + ",";
                    if (OrLogic != "")
                    {
                        intiallogic = AddjustAddingNewLogic(intiallogic, "OR", OrLogic);
                    }
                    else
                    {
                        intiallogic = AddjustAddingNewLogic(intiallogic, "OR", contactname);
                    }

                    // intiallogic = AddjustAddingNewLogic(intiallogic, "OR", contactname);
                }
                else
                {
                    intiallogic = "OR(" + contactname + ",";
                }
            }
            else
            {
                //  intiallogic = "OR(" + intiallogic + ",";//study
                if (OrLogic != "")
                {
                    UpdateLogicWcompletepath(ref OrLogic, ref intiallogic, contactname);
                    intiallogic = AddjustAddingNewLogic(intiallogic, "OR", OrLogic);
                }
            }
        }

        private static void UpdateLogicWcompletepath(ref string OrLogic, ref string intiallogic, string contactname)
        {
            string lastlogic = "";
            OrLogic = OrLogic.Trim();
            intiallogic = intiallogic.Trim();
            string outputremainlogic, lastinputs; int inputpin = 0;
            intiallogic = intiallogic.Trim();

            Analaizelogicstatement(intiallogic, ref lastlogic, out outputremainlogic, out lastinputs, out inputpin);
            string laststatment = lastlogic + lastinputs;
            laststatment = laststatment.Replace(OrLogic, "");
            // intiallogic = intiallogic.Replace(OrLogic, "");
            intiallogic = outputremainlogic + laststatment;
            if (contactname != "")
            {
                OrLogic = OrLogic + "," + contactname + "),";
            }
            else
            {
                if (OrLogic.Contains("("))
                {
                    OrLogic = OrLogic + "),";
                }
            }

            OrLogic = OrLogic.Replace(",,", ",").Replace(",)", ")");
            OrLogic = OrLogic.Replace(", ,", ",").Replace(",)", ")");
        }

        private static void Analaizelogicstatement(string intiallogic, ref string lastlogic, out string outputremainlogic, out string lastinputs, out int countinput)
        {
            intiallogic = intiallogic.Trim();

            string revlastinputs = "";
            string remainlogic = "";
            bool start = false;
            int track = 0;
            bool end = false;
            countinput = 1;
            foreach (char mychar in intiallogic.Reverse())
            {
                if (mychar == ')')
                {
                    track--;
                }

                if (mychar == '(')
                {
                    track++;
                }
                if (mychar == ',' && track == 0)
                {
                    countinput++;
                }

                if ((track == 1 || start))
                {
                    if (((mychar == ',') || (start && mychar == '(')) && (!end))
                    {
                        if (mychar == ',')
                        {
                            end = true;
                        }
                    }
                    if (end)
                    {
                        remainlogic = remainlogic + mychar;
                    }
                    else
                    {
                        lastlogic = lastlogic + mychar;
                    }

                    start = true;
                }
                else
                {
                    revlastinputs = revlastinputs + mychar;
                }
            }
            string input = remainlogic;
            outputremainlogic = new string(input.ToCharArray().Reverse().ToArray());
            input = revlastinputs;
            lastinputs = new string(input.ToCharArray().Reverse().ToArray());
            input = lastlogic;
            lastlogic = new string(input.ToCharArray().Reverse().ToArray());
        }

        private static string CheckORwoneInput(string intiallogic)
        {
            string returnvalue = intiallogic.Trim();

            string[] stringSeparators = new string[] { "OR(", "AND(" };
            string[] conttactsNamesLit = returnvalue.Split(stringSeparators, StringSplitOptions.None);

            intiallogic = intiallogic.Replace("OR(,,", "OR(");
            intiallogic = intiallogic.Replace("OR(,", "OR(");
            intiallogic = intiallogic.Replace("AND(,,", "AND(");
            intiallogic = intiallogic.Replace("AND(,", "AND(");
            intiallogic = intiallogic.Replace(",,", ",");

            string lastlogic = "";
            // bool start = false;
            intiallogic = intiallogic.Trim();

            //int track = 0;
            // string revlastinputs = "";
            //string remainlogic = "";
            string lastinputs = "";
            string outputremainlogic = "";
            int inputpin = 0;
            // bool end = false;
            //foreach (char mychar in intiallogic.Reverse())
            //{
            //    if (mychar == ')')
            //    {
            //        track--;
            //    }

            //    if (mychar == '(')
            //    {
            //        track++;
            //    }

            //    if ((track == 1 | start))
            //    {
            //        if (((mychar == ',') || (start && mychar == '(')) && (!end))
            //        {
            //            if (mychar == ',')
            //            {
            //                end = true;
            //            }
            //        }
            //        if (end)
            //        {
            //            remainlogic = remainlogic + mychar;
            //        }
            //        else
            //        {
            //            lastlogic = lastlogic + mychar;
            //        }

            //        start = true;
            //    }
            //    else
            //    {
            //        revlastinputs = revlastinputs + mychar;
            //    }
            //}

            Analaizelogicstatement(intiallogic, ref lastlogic, out outputremainlogic, out lastinputs, out inputpin);
            // string input = remainlogic;
            // string outputremainlogic = new string(input.ToCharArray().Reverse().ToArray());

            //input = revlastinputs;
            // string lastinputs = new string(input.ToCharArray().Reverse().ToArray());

            //   input = lastlogic;
            //   lastlogic = new string(input.ToCharArray().Reverse().ToArray());

            stringSeparators = new string[] { "," };
            if (lastinputs.EndsWith(","))
            {
                lastinputs = lastinputs.Substring(0, lastinputs.Length - 1);
            }
            conttactsNamesLit = lastinputs.Split(stringSeparators, StringSplitOptions.None);
            if (conttactsNamesLit.Count() == 1 && outputremainlogic != "")
            {
                returnvalue = outputremainlogic + conttactsNamesLit[0] + ",";
            }
            else
            {
                if (conttactsNamesLit.Count() == 1 && outputremainlogic == "")
                {
                    returnvalue = conttactsNamesLit[0];
                }
            }
            if (conttactsNamesLit.Count() > 1 && inputpin == 1)
            {
                //string internallastlogic = GetlastLogic(ref lastinputs);

                //if (internallastlogic == "")
                //{
                //    outputremainlogic = ""; string ttlastinputs = "";
                //    Analaizelogicstatement(lastinputs, ref internallastlogic, out outputremainlogic, out ttlastinputs);
                if (outputremainlogic != "")
                {
                    returnvalue = outputremainlogic + lastinputs; ;
                }
                else
                {
                    returnvalue = lastinputs;
                }

                //}
            }

            if (!returnvalue.EndsWith(")") && conttactsNamesLit.Count() > 1)
            {
                returnvalue = returnvalue + ")";
            }
            return returnvalue;
        }

        private static string GetContactFN(string modcontactname, string contctfn, string setpoint)
        {
            string contacttype = contctfn.Substring(contctfn.IndexOf(']') + 1, (contctfn.IndexOf('[') - contctfn.IndexOf(']')) - 1);
            string contactname = modcontactname;
            string sep = "";
            setpoint = setpoint.Replace(" ", "");
            if (setpoint != "")
            {
                sep = ",";
            }
            if (contacttype == "/")
            {
                contactname = "!" + modcontactname;
            }
            if (contacttype == "^")
            {
                //  contactname = "R_TRIG(" + modcontactname + ")";
                contactname = "^/" + modcontactname + "/"; ;
            }
            if (contacttype == "v")
            {
                //  contactname = "F_TRIG(" + modcontactname + ")";
                contactname = "v/" + modcontactname + "/";
            }
            if (contacttype == ">=")
            {
                //contactname = "GE(" + modcontactname + ")";
                contactname = ">=/" + modcontactname + sep + setpoint + "/"; ;
            }
            if (contacttype == "<=")
            {
                //contactname = "GE(" + modcontactname + ")";
                contactname = "<=/" + modcontactname + sep + setpoint + "/"; ;
            }

            if (contacttype == ">")
            {
                contactname = ">/" + modcontactname + sep + setpoint + "/"; ;
            }
            if (contacttype == "<")
            {
                contactname = "</" + modcontactname + sep + setpoint + "/"; ;
            }

            if (contacttype == "==")
            {
                contactname = "==/" + modcontactname + sep + setpoint + "/"; ;
            }
            if (contacttype == "<>")
            {
                contactname = "<>/" + modcontactname + sep + setpoint + "/"; ;
            }
            return contactname;
        }

        private static string manualedit(string logic)
        {
            logic = logic.Trim();
            if (logic.Contains("OR(,,OR"))
            {
                logic = logic.Replace("OR(,,OR", "OR(");
            }
            if (logic.EndsWith(","))
            {
                logic = logic.Substring(0, logic.Length - 1);
            }

            //if (logic.Contains("OR(") && !logic.Contains(","))
            //{
            //    logic = logic.Replace("OR(", "");
            //}

            if (logic.Contains(",,"))
            {
                logic = logic.Replace(",,", ",");
            }
            if (logic.Contains("OR(,"))
            {
                logic = logic.Replace("OR(,", "OR(");
            }
            if (logic.Contains(",)"))
            {
                logic = logic.Replace(",)", ")");
            }
            if (logic.Contains(")") && !logic.Contains("("))
            {
                logic = logic.Replace(")", "");
            }

            if (logic.Contains(", "))
            {
                logic = logic.Replace(", ", ",");
            }
            return logic;
        }

        private static void AddNewContact(string contactname, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> IDS, ref List<contact> mycontactList, string contacttype, string setpoint, string networknumber)
        {
            contact mycon = AddContact(contactname, myTemplate, IDS, contacttype);
            mycon.Networknumber = networknumber;
            if (contactname == "")
            {
            }
            if (!mycontactList.Exists(x => x.localId == mycon.localId))
            {
                mycontactList.Add(mycon);
            }
            else
            {
            }
            if (setpoint.StartsWith("_"))
            {
                setpoint = setpoint.Replace("_", "-");
            }

            if (!CheckHexnumber(ref setpoint) && setpoint != "")
            {
                contact myconsetpoint = AddContact(setpoint, myTemplate, IDS, "");
                myconsetpoint.Networknumber = networknumber;
                if (!mycontactList.Exists(x => x.localId == myconsetpoint.localId))
                {
                    mycontactList.Add(myconsetpoint);
                }
            }
        }

        private static bool CheckHexnumber(ref string setpoint)
        {
            bool returnvalue = false;
            double value;
            if (setpoint.StartsWith("_") && double.TryParse(setpoint.Substring(1, setpoint.Length - 1), out value))
            {
                setpoint = setpoint.Replace("_", "-");
            }
            if (double.TryParse(setpoint, out value) || setpoint.ToUpper().Contains("0X"))
            {
                if (setpoint.Length > 2 && setpoint.Substring(0, 2).ToUpper().Contains("0X"))
                {
                    string hex = setpoint.ToUpper().Replace("0X", "");
                    int myNewInt = Int32.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                    setpoint = myNewInt.ToString();
                }
                else
                {
                    if (setpoint.Contains(".") & setpoint.StartsWith("."))
                    {
                        setpoint = "0" + setpoint;
                    }
                }
                returnvalue = true;
            }
            return returnvalue;
        }

        private static contact AddContact(string contactname, SafetyTemplate myTemplate, SortedDictionary<int, int> IDS, string contacttype)
        {
            if (contactname == "")
            {
            }
            contact mycon = CreateContact(contactname, myTemplate, IDS, contacttype);
            myTemplate.SafetycontactMap.Add(mycon.localId, mycon);

            return mycon;
        }

        private static contact CreateContact(string contactname, SafetyTemplate myTemplate, SortedDictionary<int, int> IDS, string contacttype)
        {
            contact mycon = new contact();
            string[] systemtags = new string[] { "SYSkeysw", "SYSDisabl", "SYSday", "SYShour", "SYSminute", "SYSsecond" };
            inOutVariable myvar = null;
            if (systemtags.Contains(contactname))
            {
                myvar = new inOutVariable(myTemplate.inOutVariableMap.LastOrDefault().Value);
            }
            else
            {
                myvar = new inOutVariable(myTemplate.inOutVariableMap.FirstOrDefault().Value);
            }

            int LASTID = IDS.Last().Value + 1;
            string ID = (LASTID).ToString();
            XMLOperations_safety.ModifyIOvariableID(ref myvar, LASTID.ToString());
            mycon.Name = contactname.Trim().Replace("!", "");
            mycon.typeName = contacttype.ToString();
            XMLOperations_safety.ModifyIOvariableName(ref myvar, myvar.expressionMap.Tagname, mycon.Name);
            // XMLOperations_safety.ModifyIOvariablePosition(ref myvar, myTemplate.SheetNumber);
            mycon.MyXML = new XElement(myvar.MyXML);
            mycon.MyInterfaceXML = new XElement(myvar.MyInterfaceXML);
            if (myvar.MyGlobalConfigXML != null)
            {
                mycon.MyGlobalConfigXML = new XElement(myvar.MyGlobalConfigXML);
            }
            mycon.localId = ID;
            mycon.width = myvar.width;
            IDS.Add(LASTID, LASTID);
            return mycon;
        }

        private static coil AddNewCoil(string coiltname, ref SafetyTemplate myTemplate, string coillogic, ref SortedDictionary<int, int> IDS, List<contact> mycontactList, string timer, string coiltype, string parrallelcoil, List<string> NetworkDescription, string networknumber)
        {
            coil mycon = Addcoil(ref coiltname, myTemplate, ref coillogic, IDS, timer, parrallelcoil, NetworkDescription, networknumber);

            mycon.Coiltype = coiltype;
            int timerval = 0;

            if ((coiltype == "TDD" || coiltype == "TDE" || coiltype == "PLS") && !int.TryParse(timer, out timerval))
            {
                AddNewContact(timer, ref myTemplate, ref IDS, ref mycontactList, "", "", networknumber);
            }
            UpdatcoilContactList(ref mycon, mycontactList);
            return mycon;
        }

        private static coil Addcoil(ref string coiltname, SafetyTemplate myTemplate, ref string coillogic, SortedDictionary<int, int> IDS, string timer, string parrallelcoil, List<string> NetworkDescription, string networknumber)
        {
            coil mycon = new coil();
            inOutVariable myvar = new inOutVariable(myTemplate.inOutVariableMap.FirstOrDefault().Value);
            int LASTID = IDS.Last().Value + 1;
            string ID = (LASTID).ToString();
            XMLOperations_safety.ModifyIOvariableID(ref myvar, LASTID.ToString());
            coiltname = coiltname.Trim();
            mycon.Parrallelcoilstring = parrallelcoil.Trim();
            mycon.NetworkDescription = new List<string>(NetworkDescription);
            mycon.Name = coiltname;
            //if (coillogic.Trim().Contains("FB04_DI"))
            //{
            //    coillogic = coillogic.Trim().Replace("," + coiltname, "");
            //}

            mycon.Logic = coillogic.Trim().Replace(",,", ",").Replace(", ,", ",");

            mycon.timer = timer;
            XMLOperations_safety.ModifyIOvariableName(ref myvar, myvar.expressionMap.Tagname, coiltname);
            mycon.MyXML = new XElement(myvar.MyXML);
            mycon.MyInterfaceXML = new XElement(myvar.MyInterfaceXML);
            if (myvar.MyGlobalConfigXML != null)
            {
                mycon.MyGlobalConfigXML = new XElement(myvar.MyGlobalConfigXML);
            }
            mycon.Networknumber = networknumber;
            IDS.Add(LASTID, LASTID);
            mycon.localId = ID;
            myTemplate.SafetycoilMap.Add(ID, mycon);

            return mycon;
        }

        private static void ADDExtralogic(ref string logic, string type, string timer)
        {
             int tt = 0;
            if (type == "TDD" && timer.Contains("#"))
            {
                logic = "TOF(" + logic + ",T#" + timer + ")";
            }

            //if (type == "TDD" && ! int.TryParse(timer, out tt))
            //{
            //    logic = "TOF(" + logic + ","+ timer + ")";
            //}
            if (type == "TDD" && !timer.Contains("#"))
            {
                logic = "TDD_I(" + logic + "," + timer + ")";
            }

            if (type == "TDE" & timer.Contains("#"))
            {
                logic = "TON(" + logic + ",T#" + timer + ")";
            }

            if (type == "TDE" & !timer.Contains("#"))
            {
                logic = "TDE_I(" + logic + "," + timer + ")";
            }

            if (type == "PLS" & timer.Contains("#") &&!int.TryParse(timer, out tt))
            {
                logic = "TP(" + logic + ",T#" + timer + ")";
            }

            //if (type == "PLS" & !timer.Contains("#") && !int.TryParse(timer, out tt))
            //{
            //    logic = "TP(" + logic + "," + timer + ")";
            //}

            if (type == "PLS" & !timer.Contains("#") )
            {
                logic = "TP_I(" + logic + "," + timer + ")";
            }

            //if (type.ToUpper() == "NOT")
            //{
            //    logic = "NOT(" + logic + ")";
            //}
        }

        private static void UpdatcoilContactList(ref coil mycon, List<contact> mycontactList)
        {
            foreach (contact mycontac in mycontactList)
            {
                if (mycon.MyLogic.LogiccontactMap.FirstOrDefault(x => x.Value.localId == mycontac.localId).Value == null)
                {
                    mycon.MyLogic.LogiccontactMap.Add(mycontac.localId, mycontac);
                }
            }
        }

        private static string AddjustAddingNewLogic(string intiallogic, string Logic, string contactname)
        {
            contactname = contactname.Trim();
            string returnstring = "";
            string lastlogic = GetlastLogic(ref intiallogic);
            string sep = "";
            if (!intiallogic.EndsWith(",") && !intiallogic.EndsWith("("))
            {
                sep = ",";
            }

            Logic = Logic.Replace("(", "");

            if ((Logic == lastlogic) || (Logic == ""))
            {
                returnstring = intiallogic.Trim() + sep + contactname.Trim() + ","; ;
            }
            else
            {
                if (intiallogic != "" && lastlogic != "")
                {
                    returnstring = intiallogic.Trim() + sep + Logic + "(" + contactname.Trim() + ",";
                }
                else
                {
                    if (intiallogic == "")
                    {
                        returnstring = Logic + "(" + contactname.Trim() + ",";
                        returnstring = returnstring.Replace(",,", ",");
                    }
                    else
                    {
                        returnstring = Logic + "(" + intiallogic.Trim() + sep + contactname.Trim() + ",";
                        returnstring = returnstring.Replace(",,", ",");
                    }
                }
            }
            return returnstring;
        }

        private static string GetlastLogic(ref string intiallogic)
        {
            intiallogic = intiallogic.Replace("OR(,,", "OR(");
            intiallogic = intiallogic.Replace("OR(,", "OR(");
            intiallogic = intiallogic.Replace("AND(,,", "AND(");
            intiallogic = intiallogic.Replace("AND(,", "AND(");
            intiallogic = intiallogic.Replace(",,", ",");

            string lastlogic = "";
            bool start = false;
            intiallogic = intiallogic.Trim();

            int track = 0;
            foreach (char mychar in intiallogic.Reverse())
            {
                if (mychar == ')')
                {
                    track--;
                }

                if (mychar == '(')
                {
                    track++;
                }

                if (track == 1 | start)
                {
                    if ((mychar == ',') || (start && mychar == '('))
                        break;
                    lastlogic = lastlogic + mychar;

                    start = true;
                }
            }
            string retchar = "";
            foreach (char mychar in lastlogic.Reverse())
            {
                retchar = retchar + mychar;
            }
            lastlogic = retchar.Replace("(", "");
            return lastlogic;
        }

        private static bool ReachEndOfLogic(ref string logic)
        {
            bool returnvalue = false;
            int track = 0;
            logic = logic.Trim();
            foreach (char mychar in logic.Reverse())
            {
                if (mychar == ')')
                {
                    track--;
                }

                if (mychar == '(')
                {
                    track++;
                }
            }
            string outputremainlogicp, lastinputsp, lastlogicNEWp = ""; int inputpinp = 0;
            Analaizelogicstatement(logic, ref lastlogicNEWp, out outputremainlogicp, out lastinputsp, out inputpinp);

            if (track >= 1 && inputpinp != 1)
            {
                returnvalue = true;
                for (int i = 1; i <= track; i++)
                {
                    logic = logic + ")";
                }
            }
            if (track == 0)
            {
                if (logic.EndsWith(","))
                {
                    logic = logic.Substring(0, logic.Length - 1);
                }
            }
            Removeextralogic(ref logic);
            return returnvalue;
        }

        private static string Removeextralogic(ref string logic)//need update
        {
            string returnvalue = logic;
            string[] stringSeparators = new string[] { "OR(", "AND(" };
            string[] conttactsNamesLit = logic.Split(stringSeparators, StringSplitOptions.None);
            if (conttactsNamesLit.Length > 2)
            {
                if ((conttactsNamesLit.Length == 3) && conttactsNamesLit.LastOrDefault().EndsWith("))") && conttactsNamesLit[1] == "")
                {
                    logic = logic.Substring(logic.IndexOf("(") + 1, logic.Length - logic.IndexOf("(") - 2);
                }

                if ((conttactsNamesLit.Length == 6) && conttactsNamesLit.LastOrDefault().EndsWith("))") && conttactsNamesLit[0] == "" && conttactsNamesLit[1] == ""
                    && conttactsNamesLit[2] == "" && conttactsNamesLit[3] == "")
                {
                    logic = logic.Substring(logic.IndexOf("(") + 1, logic.Length - logic.IndexOf("(") - 2);
                    logic = logic.Substring(logic.IndexOf("(") + 1, logic.Length - logic.IndexOf("(") - 2);
                }
            }

            if ((conttactsNamesLit.Length == 3) && conttactsNamesLit[1] == "" && conttactsNamesLit[0] == "" && logic.Contains(",!") && logic.Substring(0, 9) == "AND(OR(^/")
            {
                //logic = logic.Replace("(", "").Replace(")", "").Replace("ANDOR^/", "FB04_DI(") + ")";
                //logic = logic.Replace("/", "");
            }
            stringSeparators = new string[] { "OR(" };
            string[] conttactsNamesLitOR = logic.Split(stringSeparators, StringSplitOptions.None);
            if ((conttactsNamesLit.Length == 6) && conttactsNamesLit[2] == "" && conttactsNamesLit[1] == "" && conttactsNamesLit[0] == "" && logic.Contains("OR") && logic.Contains("AND"))//CHECK SPECIAL OR
            {
                //logic = logic.Replace("AND", "OR");
            }
            return logic;
        }

        private void GetCoilName(string ContactCoilNames, ref string intiallogic, ref string coilname, string type, string timer, List<coil> mycoilList)
        {
            string[] conttactsNamesLit;
            if (ContactCoilNames.Contains("|"))
            {
                string[] stringSeparators = new string[] { "         ", "|" };
                conttactsNamesLit = ContactCoilNames.Split(stringSeparators, StringSplitOptions.None);

                conttactsNamesLit = conttactsNamesLit.Where(x => x != "").ToArray();
            }
            else
            {
                conttactsNamesLit = ContactCoilNames.Split(' ');
                conttactsNamesLit = conttactsNamesLit.Where(x => x != "").ToArray();
            }
            coilname = conttactsNamesLit.Last().Replace("-", "_").Replace(" ", "").Trim();
            if (type == "LCH" || type == "RST" || type == "TMR" || type == "CTU")
            {
                coilname = coilname + "_" + type;
                CheckNameForLCH(ref coilname, mycoilList);
            }
            if (type != "")
            {
                ADDExtralogic(ref intiallogic, type, timer);
            }

            intiallogic = coilname + "=" + intiallogic;
        }

        private static void GetName(string conttactsNames, int i, ref string intiallogic)
        {
            string[] stringSeparators = new string[] { "         ", "|" };
            //string[] conttactsNamesLit = conttactsNames.Split(stringSeparators, StringSplitOptions.None);
            List<string> conttactsNamesLit = new List<string>();
            int k = 1;
            string returnstring = "";
            foreach (char mych in conttactsNames)
            {
                returnstring = returnstring + mych;
                if (k % 10 == 0)
                {
                    conttactsNamesLit.Add(returnstring);
                    returnstring = "";
                }
                k++;
            }
            if (returnstring != "")
            {
                conttactsNamesLit.Add(returnstring);
            }
            //if(conttactsNames.Contains("|"))
            //{
            //    conttactsNamesLit= conttactsNames.Split('|');
            //    conttactsNamesLit = conttactsNamesLit.Where(x => x != "").ToArray();
            //}
            //else
            //{
            //    conttactsNamesLit= conttactsNames.Split(' ');
            //    conttactsNamesLit = conttactsNamesLit.Where(x => x != "").ToArray();
            //}
            // conttactsNamesLit = conttactsNamesLit.Where(x => x != "").ToArray();
            int pos = i / 10;

            if (pos <= conttactsNames.Length)
            {
                if (intiallogic == "")
                {
                    intiallogic = conttactsNamesLit[pos].Replace("|", "").Trim();
                }
                else
                {
                    // intiallogic = intiallogic +","+ conttactsNamesLit[pos].Trim();
                    intiallogic = conttactsNamesLit[pos].Replace("|", "");
                }
            }

            intiallogic = intiallogic.Replace("-", "_").Replace(" ", "").Trim();
            if (intiallogic.Contains("$"))
            {
                intiallogic = intiallogic.Replace("$", "SYS");
            }

            if (intiallogic.ToUpper() == "TIME")
            {
                intiallogic = "SYS" + intiallogic;
            }

            CheckHexnumber(ref intiallogic);

            //throw new NotImplementedException();
        }

        //SerializeSafetyParToDec
        public static Dictionary<SafetyBlock, List<KeyValuePair<variable, inOutVariable>>> DeserializeSafetyParToDec(SafetyTemplate st)
        {
            Dictionary<SafetyBlock, List<KeyValuePair<variable, inOutVariable>>> parToDeclMapSafety = new Dictionary<SafetyBlock, List<KeyValuePair<variable, inOutVariable>>>();

            foreach (SafetyBlock cb in st.SafetyBlockMap.Values)
            {
                parToDeclMapSafety.Add(cb, new List<KeyValuePair<variable, inOutVariable>>());
            }

            foreach (inOutVariable IOS in st.inOutVariableMap.Values)
            {
                if (IOS.connectionMap.refLocalId != null)
                {
                    string parts = IOS.connectionMap.refLocalId;

                    string PINNAME = IOS.connectionMap.formalParameter;

                    SafetyBlock cbSOURCE = st.SafetyBlockMap[parts];

                    if (PINNAME == null)
                    {
                        variable from = cbSOURCE.outputVariablesMap.variableMap.First().Value;

                        parToDeclMapSafety[cbSOURCE].Add(new KeyValuePair<variable, inOutVariable>(from, IOS));
                    }
                    else
                    {
                        variable from = cbSOURCE.outputVariablesMap.variableMap[PINNAME];

                        parToDeclMapSafety[cbSOURCE].Add(new KeyValuePair<variable, inOutVariable>(from, IOS));
                    }
                }
            }

            return parToDeclMapSafety;
        }

        //SerializeSafetyDecToPar
        public static Dictionary<SafetyBlock, List<KeyValuePair<inOutVariable, variable>>> DeserializeSafetyDecToPar(SafetyTemplate st)
        {
            Dictionary<SafetyBlock, List<KeyValuePair<inOutVariable, variable>>> declToParMapSafety = new Dictionary<SafetyBlock, List<KeyValuePair<inOutVariable, variable>>>();

            foreach (SafetyBlock cb in st.SafetyBlockMap.Values)
            {
                List<KeyValuePair<inOutVariable, variable>> declList = new List<KeyValuePair<inOutVariable, variable>>();
                declToParMapSafety.Add(cb, declList);
                //parToDeclMapSafety.Add(cb, new List<KeyValuePair<variable, inOutVariable>>());

                foreach (variable varib in cb.inputVariablesMap.variableMap.Values)
                {
                    string parts = varib.connectionPointInMap.connectionMap.refLocalId;

                    if (parts != null)
                    {
                        string PINNAME = varib.connectionPointInMap.connectionMap.formalParameter;
                        if (PINNAME == null)
                        {
                            if (st.inOutVariableMap.ContainsKey(parts))
                            {
                                inOutVariable inOutVar = st.inOutVariableMap[parts];
                                declList.Add(new KeyValuePair<inOutVariable, variable>(inOutVar, varib));
                            }
                            else
                            {
                                // variable from = st.SafetyBlockMap[parts].outputVariablesMap.variableMap.First().Value;//need to modify output to be one variable only

                                //parToParListSafety.Add(new KeyValuePair<variable, variable>(from, varib));
                            }
                        }
                        else
                        {
                            //variable from = st.SafetyBlockMap[parts].outputVariablesMap.variableMap[PINNAME];

                            // parToParListSafety.Add(new KeyValuePair<variable, variable>(from, varib));
                        }
                    }
                }
            }

            return declToParMapSafety;
        }

        //SerializeSafetyParToPar
        public static List<KeyValuePair<variable, variable>> DeserializeSafetyParToPar(SafetyTemplate st)
        {
            List<KeyValuePair<variable, variable>> ParToParMapSafety = new List<KeyValuePair<variable, variable>>();

            foreach (SafetyBlock cb in st.SafetyBlockMap.Values)
            {
                foreach (variable varib in cb.inputVariablesMap.variableMap.Values)
                {
                    string parts = varib.connectionPointInMap.connectionMap.refLocalId;

                    if (parts != null)
                    {
                        string PINNAME = varib.connectionPointInMap.connectionMap.formalParameter;
                        if (PINNAME == null)
                        {
                            if (!(st.inOutVariableMap.ContainsKey(parts)))
                            {
                                variable from = st.SafetyBlockMap[parts].outputVariablesMap.variableMap.First().Value;//need to modify output to be one variable only

                                ParToParMapSafety.Add(new KeyValuePair<variable, variable>(from, varib));
                            }
                        }
                        else
                        {
                            variable from = st.SafetyBlockMap[parts].outputVariablesMap.variableMap[PINNAME];

                            ParToParMapSafety.Add(new KeyValuePair<variable, variable>(from, varib));
                        }
                    }
                }
            }

            return ParToParMapSafety;
        }

        internal static Comment DeserializeSafetyComment(XElement thisIO)
        {
            Comment newcomment = new Comment();

            newcomment.localId = thisIO.Attribute("localId").Value;
            newcomment.MyXML = thisIO;

            var postionlist = from S in thisIO.Descendants()
                              where S.Name.LocalName == "position" && S.ToString().Contains("ts:SheetNumber")
                              select S;
            XElement thispostion = postionlist.First();

            if (thispostion != null)
            {
                var nameAttributes =
                                     from attr in thispostion.Attributes()
                                     where attr.Name.ToString().Contains("SheetNumber")
                                     select attr;
                foreach (var attribute in nameAttributes)
                {
                    newcomment.SheetNumber = attribute.Value;
                }
            }

            var Contentlist = from S in thisIO.Descendants()
                              where S.Name.LocalName == "content"
                              select S;
            if (Contentlist != null)
            {
                newcomment.content = Contentlist.First().Value;
            }

            return newcomment;
        }

        //semines
        public Dictionary<string, SafetyTemplate> DeserializeSeminesSafetyTemplate(string[] ioFlgList, string ProgramName, ref List<string> mylogic, ref SortedDictionary<int, int> IDS, ref List<string> Library, DataTable myDB)
        {
            Dictionary<string, SafetyTemplate> newSafetyTemplateMap = new Dictionary<string, SafetyTemplate>();
            List<coil> mycoillist = new List<coil>();

            var CompoundList = ioFlgList.Select((Value, Index) => new { Value, Index })
                 .Where(p => p.Index%MAXNETWORKCOUNT==0).ToList();
            if ((CompoundList != null) && (CompoundList.Count() != 0))
            {
                int thisCompoundindex = 0;
                foreach (var thisCompound in CompoundList)
                {
                    int upperIndex;
                    if (thisCompoundindex + 1 < CompoundList.Count())
                    {
                        upperIndex = CompoundList[thisCompoundindex + 1].Index;
                    }
                    else
                    {
                        upperIndex = ioFlgList.Count();
                    }

                    int lowerIndex = thisCompound.Index;
                    //Compound newCompound = new Compound();

                    thisCompoundindex++;

                    try
                    {
                        DeserializeSheetSemines(ioFlgList.ToList().GetRange(lowerIndex, upperIndex - lowerIndex), ref newSafetyTemplateMap, ref mycoillist, ProgramName, ref mylogic, ref IDS, ref Library, myDB);
                    }
                    catch (Exception ex)
                    {
                        string tt = ex.Message;
                    }
                }
            }

            return newSafetyTemplateMap;
        }
        private void DeserializeSheetSemines(List<string> list, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref List<coil> mycoillist, string programName, ref List<string> mylogic, ref SortedDictionary<int, int> IDS, ref List<string> Library, DataTable myDB)
        {
            string pagenumber = list[0].Split('@')[2].Trim();
            string networks = "";
            // SafetyTemplate mynewSafetyTemplate = null;

            string sheettitle = "";
            // SafetyTemplate mynewSafetyTemplate = null;
            //
            SafetyTemplate mynewSafetyTemplate = newSafetyTemplateMap.Values.LastOrDefault();
            int safetytemplatescount = newSafetyTemplateMap.Count() + 2;
            if (mynewSafetyTemplate == null)
            {
                CreateFBDSafetyTemplate(ref mynewSafetyTemplate);

                mynewSafetyTemplate.SheetNumber = safetytemplatescount.ToString();

                mynewSafetyTemplate.ProgramName = programName;
                Duplicatecomments(ref mynewSafetyTemplate, ref IDS);
                XMLOperations_safety.ModifySafetTemplatePosition(ref mynewSafetyTemplate, mynewSafetyTemplate.SheetNumber);
                sheettitle = "Ref:" + pagenumber + " Network #";
            }
            else
            {
                if(mynewSafetyTemplate.Name.Contains("Ref:" + pagenumber))
                  sheettitle = mynewSafetyTemplate.Name + " " + ",Network #";
                else
                    sheettitle = mynewSafetyTemplate.Name + " "+ "Ref:" + pagenumber + " Network #";
            }
            //


            // int safetytemplatescount = newSafetyTemplateMap.Count() + 5;

            int networkcountinsheet = 0;

            //if (mynewSafetyTemplate.SheetNumber != null)
            //{
            //    newSafetyTemplateMap.Add(mynewSafetyTemplate.SheetNumber, mynewSafetyTemplate);
            //}

            int lowerIndex = 0;
            int thisCompoundindex = 0;
                foreach (var thisCompound in list)
                {
                  

                    string first = "";
                    string last = "";

                    string networkenumber = thisCompound.Split('@')[1].Trim();
                    if (networks != "")
                    {
                        networks = networks + "," + networkenumber;
                    }
                    else
                    {
                        networks = sheettitle.EndsWith("#") ? sheettitle + networkenumber : sheettitle + "," + networkenumber;

                    }

                    sheettitle = networks;
                    if (sheettitle.Length >= 45)
                    {
                        string[] values = sheettitle.Split(',');
                        first = values[0];
                        last = values.LastOrDefault();

                        if (sheettitle.Contains("To") && !sheettitle.Contains(","))
                        {

                            values = Regex.Split(sheettitle, "To"); ;

                            first = values[0];
                            last = values.LastOrDefault();

                        }

                        sheettitle = first + " To " + last;
                    }
                    int upperIndex=1;
             

                   
                    //Compound newCompound = new Compound();

                    thisCompoundindex++;
                    List<string> ComundList, TempList;
                    PrepareTheBasicSafetySheet(list, newSafetyTemplateMap, programName, ref IDS, pagenumber, ref networks, ref sheettitle, ref mynewSafetyTemplate, ref safetytemplatescount, networkenumber, upperIndex, lowerIndex, out ComundList, out TempList, mylogic, "Ref:");
                    if (TempList.Count() == 0)
                    {
                        updatesheettitles(sheettitle, ref mynewSafetyTemplate);
                    }
                    else
                    {
                        updatesheettitles(sheettitle, ref mynewSafetyTemplate);
                    }

                DeserializeNetworkSemines(list.ToList().GetRange(lowerIndex, 1), ref mynewSafetyTemplate, ref newSafetyTemplateMap, ref mycoillist, ref mylogic, ref IDS, ref Library, myDB, pagenumber+" "+ networkenumber);

                    if (mynewSafetyTemplate.SheetNumber != null)
                    {
                        var sheet = newSafetyTemplateMap.FirstOrDefault(ke => ke.Key == mynewSafetyTemplate.SheetNumber).Value;
                        if (sheet == null)
                        {
                            newSafetyTemplateMap.Add(mynewSafetyTemplate.SheetNumber, mynewSafetyTemplate);
                        }
                    }
                    if (ComundList.Count() != 0 || TempList.Count() != 0)
                    {
                        networks = "";
                    }
                    else
                    {
                        if (!mynewSafetyTemplate.Name.Contains("PACK"))
                        {
                            networkcountinsheet++;
                        }
                    }
                lowerIndex = lowerIndex + 1;
            }
            
        }
        private void DeserializeNetworkSemines(List<string> list, ref SafetyTemplate mynewSafetyTemplate, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref List<coil> mycoillist, ref List<string> mylogic, ref SortedDictionary<int, int> IDS, ref List<string> Library, DataTable myDB, string networknumber)
        {
            try
            {
                string Logic = "";

                int begin = 0;
                List<contact> mycontactlist = new List<contact>();
                List<string> networkDiscption = SerializeDescription(list);
                if (list.FirstOrDefault().Contains("PROGRAM"))
                {
                    GetnewProgramName(ref mynewSafetyTemplate, list.FirstOrDefault());
                }

                if (!SerializePack(list, ref Logic, ref begin, 1000, ref mynewSafetyTemplate, ref IDS, ref mycontactlist) && !CheckIfmixedSTandLadder(list, networknumber, mylogic) &&

                        !CheckAndUpdateTemplateSafetyTemplate(list, networkDiscption, ref mynewSafetyTemplate, newSafetyTemplateMap, networknumber, ref IDS)
                   )
                {
                    networkDiscption = ModifyCommentNetworkFromSpacer(mynewSafetyTemplate, networkDiscption, networknumber);
                    // SerializeST(list, ref mynewSafetyTemplate, ref newSafetyTemplateMap, networkDiscption, ref IDS, ref Library, myDB, networknumber, mylogic);
                    SerializeLogicModSemines(list, ref Logic, ref begin, 1000, ref mynewSafetyTemplate, ref IDS, ref mycontactlist, ref mycoillist, ref Library, myDB, networkDiscption, ref newSafetyTemplateMap, networknumber);

                    calculateDescrptionSize(mynewSafetyTemplate, networkDiscption);
                    calculateParrallelSize(mynewSafetyTemplate);
                    mynewSafetyTemplate.Networkcount++;

                    ADDLatchingcoils(ref Logic, ref mynewSafetyTemplate, ref IDS, ref mycontactlist, ref mycoillist, myDB, networkDiscption, ref newSafetyTemplateMap, networknumber);

                    Checkbugexist(ref mynewSafetyTemplate, Logic);
                }

                mylogic.Add(Logic);
            }
            catch (Exception EX)
            {
                mylogic.Add("Network # " + networknumber + ": can't be converted:this network need manual work");
                string TT = EX.Message;
            }
        }
        private void SerializeLogicModSemines(List<string> list, ref string logic, ref int posbegin, int posend, ref SafetyTemplate myTemplate, ref SortedDictionary<int, int> IDS, ref List<contact> mycontactlist, ref List<coil> mycoillist, ref List<string> Library, DataTable myDB, List<string> NetworkDiscption, ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, string networknumber)
        {
            foreach (var line in list) 
            {
                logic=line.Split('@')[0].Replace(" ","");
                string coilname = logic.Split('=')[0];
                coil mycoilorg = Addcoil(ref coilname, myTemplate, ref logic, IDS, "", "", NetworkDiscption, networknumber);
                foreach(var contact in line.Split('@')[3].Split('/').Where(con=>!con.Contains("#")))
                {
                    string modCont = contact.Replace("(", "").Replace(")", "").Trim().Replace(" ","");

                    string contactType = "";
                    if (modCont.Contains("!"))
                    {
                        contactType = "/";
                    }
                    contact mycon = AddContact(modCont, myTemplate, IDS, contactType);
                    mycoilorg.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
                    
                    mycontactlist.Add(mycon);
                    mycoillist.Add(mycoilorg);
                }
              

                myTemplate.Networkcount++;
                
            }
        }
            //semines

        }
}