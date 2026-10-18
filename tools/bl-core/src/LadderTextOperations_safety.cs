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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BL.SafetyData;
using System.Data;
using System.IO;

namespace BL
{
    
    public class LadderTextOperations_safety
    {
       public static Dictionary<string, inOutVariable> globalVariableMap;
        public static DataTable MyDB;
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

                    ReadBlocks(ref newSafetyTemplateMap, PrgInioFlgList, ref IDS);
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

        public  List<program> DeserializeSafetyProgram(string[] loadedXml,ref string outputfil,string OrginalXMLFileName)
        {

            XMLOperations_LadderLogic.Intialize();

            //var ioFlgList = (from b in loadedXml.Descendants()
            //                 where b.Name.LocalName == "pou" && b.Attribute("pouType").Value == "program"
            //                 select b).ToList();

            //var configurations = (from b in loadedXml.Descendants()
            //                      where b.Name.LocalName == "configurations"
            //                      select b).FirstOrDefault();

            var PROGRAMList = (from e in loadedXml
                                 where e.Replace(" ","").Contains("TRISTATION")
                                 select e).FirstOrDefault();
          

            List<program> newPrograms = new List<program>();
            LadderTextOperations_safety.globalVariableMap = new Dictionary<string, inOutVariable>();
            Dictionary<string, inOutVariable> globalVariableMap = new Dictionary<string, inOutVariable>();

            //  int i = 1;

            //foreach (var PrgInioFlgList in PROGRAMList)
            //{
                if (PROGRAMList != null)
                {
                  string programname= PROGRAMList.Split(':')[2].Trim().Split(' ')[0]; 
                    if (newPrograms.FirstOrDefault(pr=>pr.Name== programname)==null)
                    {

                    var configurations = (from b in XMLOperations_LadderLogic.blankPT2.Descendants()
                                          where b.Name.LocalName == "configurations"
                                          select b).FirstOrDefault();
                    var variableList = (from HH in configurations.Descendants()
                                        where HH.Name.LocalName == "globalVars" 
                                        select HH).FirstOrDefault();
                    //  program newProgram = new program();
                    program newProgram = XMLOperations_LadderLogic.BlankProgram.LastOrDefault(pr=>!pr.Name.Contains("TEMPLATES"));
                    newProgram.MyXML = XMLOperations_LadderLogic.BlankProgram.LastOrDefault(pr => !pr.Name.Contains("TEMPLATES")).MyXML;
                    newProgram.globalVarsXML= XMLOperations_LadderLogic.BlankProgram.LastOrDefault(pr => !pr.Name.Contains("TEMPLATES")).globalVarsXML;
                 
                    newProgram.Name = programname.Replace("-", "_");
                    XMLOperations_safety.ModifyProgramName(ref newProgram, newProgram.Name);
                  
                    newProgram.pouType = "program";
                
                    List<string> mylogic = new List<string>();
                    SortedDictionary<int, int> IDS = new SortedDictionary<int, int>();
                    List<string> Library = new List<string>();
                    IDS.Add(200, 200);
                    TXTProgramOperations_Safety sf = new TXTProgramOperations_Safety();
                    Dictionary<string, SafetyTemplate> newSafetyTemplateMap = sf.DeserializeSafetyTemplate(loadedXml, newProgram.Name,ref mylogic,ref IDS,ref Library,MyDB);
                    CreatelogicFileText(newSafetyTemplateMap,outputfil);
                    CreateLibraryandAdd(Library, ref XMLOperations_LadderLogic.blankPT2);
                    CreateSTSettinandAdd(ref XMLOperations_LadderLogic.blankPT2);
                  
                    ADDSafetyXmlToProgram(newSafetyTemplateMap, ref newProgram, ref variableList, MyDB);//need to study
                    
                    XElement myproxml = newProgram.MyXML;
                  
                     XMLOperations_LadderLogic.BuildCoillogic(ref newSafetyTemplateMap, ref IDS, ref myproxml, mylogic);
                    CreatNewPrograms(newSafetyTemplateMap, ref variableList, MyDB);

                    newProgram.MyLogicList = mylogic;
                    newProgram.IDsMap = IDS;
             
                    newPrograms.Add(newProgram);
               
                     outputfil = seTfbfolder( OrginalXMLFileName);
                    SetAndSelectOutXML(TXTProgramOperations_Safety.TemplateXMLFile, XMLOperations_LadderLogic.blankPT2,outputfil);
                  //  XMLOperations_LadderLogic.blankPT2.Save(outputfil);
            


                    }
                }
            //}

            return newPrograms;
            
        }

        private void CreatelogicFileText(Dictionary<string, SafetyTemplate> newSafetyTemplateMap,string outputfil)
        {   List<string> list = new List<string>();
            foreach(var template in newSafetyTemplateMap.Values)
            {
                foreach(var coil in template.SafetycoilMap.Values)
                {

                    list.Add(coil.Networknumber+":"+coil.Logic); 
                }

            }
            string logDir = System.IO.Path.GetDirectoryName(outputfil);
            if (string.IsNullOrEmpty(logDir)) logDir = ".";
            File.WriteAllLines(System.IO.Path.Combine(logDir, "output.logic.txt"), list);
        }

        private void CreateSTSettinandAdd(ref XElement blankPT2)
        {
            if( TXTProgramOperations_Safety.STConstantProgram)
            {
                string config = "PROGRAM SetPoints" + Environment.NewLine + "VAR_EXTERNAL" + Environment.NewLine;
                if (TXTProgramOperations_Safety.mySTprogram.Any())
                {
                   
                    foreach (var txt in TXTProgramOperations_Safety.mySTConfiguration)
                    {
                        config = config + txt + Environment.NewLine;
                    }
                    config = config + "END_VAR" + Environment.NewLine;
                }
                
                if (TXTProgramOperations_Safety.mySTprogram.Any())
                {

                    foreach (var txt in TXTProgramOperations_Safety.mySTprogram)
                    {
                        config = config + txt + Environment.NewLine;
                    }
                    config = config + "END_PROGRAM" + Environment.NewLine;
                }
                if(TXTProgramOperations_Safety.mySTprogram.Any()&& TXTProgramOperations_Safety.mySTprogram.Any())
                {
                    XMLOperations_LadderLogic.DeserializeSafetySTProgram(config, ref blankPT2);
                }
            }
        }

        private void SetAndSelectOutXML(XElement templateFile, XElement blankPT2, string outputfil)
        {
            if(templateFile!=null )
            {
                var pt2programs = (from d in blankPT2.Descendants()
                                  where d.Name.LocalName == "pou"
                                  select d).ToList();
               

                foreach (var program in pt2programs)
                {
                    AddFBXmlToPT2(program,ref templateFile);
                }

                templateFile.Save(outputfil);
            }
            else
            {
                XMLOperations_LadderLogic.blankPT2.Save(outputfil);
            }
        }
        private static void AddFBXmlToPT2(XElement addedprogram, ref XElement newProgram)
        {
            var pt2program = (from d in newProgram.Descendants()
                              where d.Name.LocalName == "pous"
                              select d).FirstOrDefault();
            pt2program.AddFirst(addedprogram);
        }
        private static string seTfbfolder(  string OrginalXMLFileName)
        {
            string fullPath = System.IO.Path.GetDirectoryName(OrginalXMLFileName);
            string OrginalXMLwithoutExt = System.IO.Path.GetFileNameWithoutExtension(OrginalXMLFileName);
            //string RulebookNamewithoutExt = System.IO.Path.GetFileNameWithoutExtension(RulebookName);
            fullPath = System.IO.Path.Combine(fullPath, "FB" + "_" + OrginalXMLwithoutExt);

            if (!System.IO.Directory.Exists(fullPath))
            {
                System.IO.Directory.CreateDirectory(fullPath);
            }
            string outputPT2File = System.IO.Path.Combine(fullPath, OrginalXMLwithoutExt + "_FB.XML");
            return outputPT2File;
        }

        private static void CreateLibraryandAdd(List<string> library,ref XElement newProgram)
        {
            //throw new NotImplementedException();
         XMLOperations_LadderLogic.DeserializeSafetyfnblock(library,ref newProgram);
        }

        private static void ADDSafetyXmlToProgram(Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref program newProgram,ref XElement  myconfiguration, DataTable MyDB)
        {
            
            foreach (var mytemplate in newSafetyTemplateMap.Values)
            {
                XElement mi = newProgram.MyXML;
                TXTManager tt = new TXTManager();
               

                tt.AddSafetyTEmplateToProgram(mytemplate, ref mi, ref myconfiguration, MyDB,false);
            }
           

        }

        private static  void CreatNewPrograms(Dictionary<string, SafetyTemplate> newSafetyTemplateMap, ref XElement myconfiguration, DataTable MyDB)
        {
            TXTManager tt = new TXTManager();
            SafetyTemplate firstTemplate;
            List<program> newList = new List<program>();

            program addedProgram = new program();
            if ((firstTemplate = newSafetyTemplateMap.Values.FirstOrDefault(tm =>!string.IsNullOrEmpty( tm.CreateNewProgram ))) != null)
            {
                addedProgram = AddProgramTProject(firstTemplate.CreateNewProgram, firstTemplate.ProgramName);
                newList.Add(addedProgram);

                foreach (var mytemplate in newSafetyTemplateMap.Values)
                {

                    if (!string.IsNullOrEmpty(mytemplate.CreateNewProgram))
                    {

                        if ((addedProgram = XMLOperations_LadderLogic.BlankProgram.FirstOrDefault(pr => pr.Name == mytemplate.CreateNewProgram)) == null)
                        {
                            addedProgram = AddProgramTProject(mytemplate.CreateNewProgram, mytemplate.ProgramName);
                            newList.Add(addedProgram);

                        }


                    }


                    SafetyTemplate template = new SafetyTemplate(mytemplate);
                   
                       addedProgram.SafetyTemplateMap.Add( (int.Parse (template.SheetNumber)+ 1).ToString(), template);
                   
                    


                }
            }
             AddnewPrograms(myconfiguration, MyDB, newList);

        }

        private static XElement AddnewPrograms(XElement myconfiguration, DataTable MyDB, List<program> newList)
        {
            TXTManager pp = new TXTManager();
            foreach (program myprogram in newList)
            {
                int index = 1;
                foreach (var mytemplate in myprogram.SafetyTemplateMap.Values)
                {
                    if(index!=1)
                    {
                        XElement mi = myprogram.MyXML;
                        SafetyTemplate template = mytemplate;
                        XMLOperations_safety.ModifySafetTemplatePosition(ref template, index.ToString());
                        ModifyTeplateLadderPosition(ref template, index.ToString());
                        pp.AddSafetyTEmplateToProgram(template, ref mi, ref myconfiguration, MyDB,false);
                        XMLOperations_LadderLogic.AddCoilBlocksToProgram(ref template, ref mi);
                        XMLOperations_LadderLogic.AddconstanttoProgram(ref template, ref mi);
                        XMLOperations_LadderLogic.AddcommentstoProgram(ref template, ref mi);


                    }
                    index = index + 1;

                }
                XElement programs = (from HH in XMLOperations_LadderLogic.blankPT2.Descendants()
                                     where HH.Name.LocalName == "pous"
                                     select HH).FirstOrDefault();
                programs.Add(myprogram.MyXML);
            }

            return myconfiguration;
        }

        private static void ModifyTeplateLadderPosition(ref SafetyTemplate template, string newSheetNumber)
        {
            foreach (coil mycoil in template.SafetycoilMap.Values)
            {
                foreach (SafetyBlock myblock in mycoil.MyLogic.LogicBlockMap.Values)
                {
                    myblock.MyXML = new XElement(myblock.MyXML);
                    SafetyBlock testblock = myblock;

                    XMLOperations_safety.ModifyBlockPosition(ref testblock, newSheetNumber);
                }
                foreach (inOutVariable modinoutvar in mycoil.MyLogic.inOutVariableMap.Values)
                {
                    modinoutvar.MyXML = new XElement(modinoutvar.MyXML);
                    inOutVariable testio = modinoutvar;
                    XMLOperations_safety.ModifyIOvariablePosition(ref testio, newSheetNumber);
                }

                foreach (Comment modcomment in mycoil.MyLogic.CommentMap.Values)
                {
                    modcomment.MyXML = new XElement(modcomment.MyXML);
                    Comment testcomment = modcomment;
                    
                    XMLOperations_safety.ModifyCommentPosition(ref testcomment, newSheetNumber);
                }

                mycoil.MyXML = new XElement(mycoil.MyXML);
                XElement thiscoil = mycoil.MyXML;
                ModifyIOvariableXMLPosition(ref thiscoil, newSheetNumber);

            }

            foreach (contact con in template.SafetycontactMap.Values)
            {
                con.MyXML = new XElement(con.MyXML);
                XElement thiscoil = con.MyXML;
                ModifyIOvariableXMLPosition(ref thiscoil, newSheetNumber);
            }
        }

        public static void ModifyIOvariableXMLPosition(ref XElement MyXML, string newSheetNumber)
        {
            XElement ioVariable = MyXML;

            var postionlist = from S in ioVariable.Descendants()
                              where S.Name.LocalName == "position" && S.ToString().Contains("SheetNumber")
                              select S;
            XElement thispostion = postionlist.First();

            if (thispostion != null)
            {
                
                thispostion.LastAttribute.Value = newSheetNumber;//ToDo:there is problem to get attribute contain :
            }
        }


        private static program AddProgramTProject(string newprogramname,string theBigProgram)
        {
            program addedProgram = CreateNewProgram(newprogramname);
            XMLOperations_LadderLogic.BlankProgram.Add(addedProgram);
            program big = XMLOperations_LadderLogic.BlankProgram.FirstOrDefault(pr => pr.Name == theBigProgram);
            if(big!=null)
            {
                AddInterfacexml(big, addedProgram);
            }
            //XElement programs = (from HH in XMLOperations_LadderLogic.blankPT2.Descendants()
            //                     where HH.Name.LocalName == "pous"
            //                     select HH).FirstOrDefault();
            // programs.Add(addedProgram.MyXML);
            return addedProgram;
        }

        private static void AddInterfacexml(program big, program addedProgram)
        {
           var interfaBig= (from b in big.MyXML.Descendants()
                         where b.Name.LocalName == "interface"
                         select b).FirstOrDefault();

            var  interfasmall= (from b in addedProgram.MyXML.Descendants()
                                   where b.Name.LocalName == "interface"
                                   select b).FirstOrDefault();
            if(interfaBig!=null && interfasmall!=null)
            {
                interfasmall.Descendants().Remove();
                interfasmall.Add(interfaBig.Descendants());
            }


        }

        private static program CreateNewProgram(string programName)
        {
           XElement blankPT2 = FileWorks.LoadXMLFile(XMLOperations_LadderLogic.BlankPT2fullPath);

            var program= XMLOperations_LadderLogic.ReadBlankPT2(blankPT2).FirstOrDefault(pr=>!pr.Name.Contains("TEMPLATES"));

            XMLOperations_safety.ModifyProgramName(ref program, programName);
            return program;
        }

        private static void savetextlogic(List<string> myLogicList)
        {
            throw new NotImplementedException();
        }

        private static void ReadLoacalVariable(ref program newProgram)
        {
           
            foreach (SafetyTemplate sheet in newProgram.SafetyTemplateMap.Values)
            {
                foreach (inOutVariable varible in sheet.inOutVariableMap.Values)
                {
                    var locTag = newProgram.VariableMap.FirstOrDefault(val => val.Key == varible.expressionMap.Tagname).Value;
                    if(locTag==null)
                    {
                     if(varible.VariableType=="Local_Varaible")
                     {
                        newProgram.VariableMap.Add(varible.expressionMap.Tagname,varible);
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




        static void ReadIOvariable(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement ioFlgList, ref SortedDictionary<int, int> myIDs)
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


        static void ReadBlocks(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement ioFlgList, ref SortedDictionary<int, int> myIDs)
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

        static void ReadTagsConf(ref Dictionary<string, SafetyTemplate> newSafetyTemplateMap, XElement _GVList)
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
        static void ModifyIOvariablePosition(ref inOutVariable modinOutVariable, string newSheetNumber)
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
        static void ModifyIOvariableName(ref inOutVariable modinOutVariable, string OldTag, string newTag)
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

        static void ModifyBlockPosition(ref SafetyBlock modSafetyBlock, string newSheetNumber)
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

        static void ModifyCommentPosition(ref Comment modSafetyComment, string newSheetNumber)
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
                LadderTextOperations_safety.ModifyIOvariablePosition(ref testio, newSheetNumber);
            }

            foreach (SafetyBlock modblock in modSafetyTemplate.SafetyBlockMap.Values)
            {
                SafetyBlock testblock = modblock;
                LadderTextOperations_safety.ModifyBlockPosition(ref testblock, newSheetNumber);
            }

            foreach (Comment modcomment in modSafetyTemplate.CommentMap.Values)
            {
                Comment testcomment = modcomment;
                LadderTextOperations_safety.ModifyCommentPosition(ref testcomment, newSheetNumber);
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
                        LadderTextOperations_safety.ModifyIOvariableName(ref testio, OldTag, newTag);
                    }

                    if (testio.VariableType == "Local_Varaible")
                    {
                        //XMLOperations_safety.ModifyIOvariableName(ref testio, OldTag, newTag, modprogram.localVarsXML);
                        LadderTextOperations_safety.ModifyIOvariableName(ref testio, OldTag, newTag);
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

        public static void ModifySafetyBlockName(ref SafetyTemplate test2,string oldIOName ,string newIOName)
        {
            SafetyBlock modblock = test2.SafetyBlockMap.FirstOrDefault(blk => blk.Value.instanceName == oldIOName).Value;
            if (modblock != null)
            {
                modblock.instanceName = newIOName;
                modblock.BlockAttributes.BlockName = newIOName;

                if (modblock.MyXML.ToString().Contains("instanceName"))
                {
                    modblock.MyXML.Attribute("instanceName").Value=newIOName;
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
                    LadderTextOperations_safety.ModifyCommentContent(ref testio, newCommentContent);
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
        //seimens
        public List<program> DeserializeSafetyProgramSeimens(string[] loadedXml, ref string outputfil, string OrginalXMLFileName)
        {

            XMLOperations_LadderLogic.Intialize();

            //var ioFlgList = (from b in loadedXml.Descendants()
            //                 where b.Name.LocalName == "pou" && b.Attribute("pouType").Value == "program"
            //                 select b).ToList();

            //var configurations = (from b in loadedXml.Descendants()
            //                      where b.Name.LocalName == "configurations"
            //                      select b).FirstOrDefault();

            var PROGRAMList = (from e in loadedXml
                               where e.Replace(" ", "").Contains("Logic")
                               select e).FirstOrDefault();
            PROGRAMList= PROGRAMList==null?"SeimensLogic":PROGRAMList;

            List<program> newPrograms = new List<program>();
            LadderTextOperations_safety.globalVariableMap = new Dictionary<string, inOutVariable>();
            Dictionary<string, inOutVariable> globalVariableMap = new Dictionary<string, inOutVariable>();

            //  int i = 1;

            //foreach (var PrgInioFlgList in PROGRAMList)
            //{
            if (PROGRAMList != null)
            {
                string programname = PROGRAMList.Split('@')[0].Trim();
                if (newPrograms.FirstOrDefault(pr => pr.Name == programname) == null)
                {

                    var configurations = (from b in XMLOperations_LadderLogic.blankPT2.Descendants()
                                          where b.Name.LocalName == "configurations"
                                          select b).FirstOrDefault();
                    var variableList = (from HH in configurations.Descendants()
                                        where HH.Name.LocalName == "globalVars"
                                        select HH).FirstOrDefault();
                    //  program newProgram = new program();
                    program newProgram = XMLOperations_LadderLogic.BlankProgram.LastOrDefault(pr => !pr.Name.Contains("TEMPLATES"));
                    newProgram.MyXML = XMLOperations_LadderLogic.BlankProgram.LastOrDefault(pr => !pr.Name.Contains("TEMPLATES")).MyXML;
                    newProgram.globalVarsXML = XMLOperations_LadderLogic.BlankProgram.LastOrDefault(pr => !pr.Name.Contains("TEMPLATES")).globalVarsXML;

                    newProgram.Name = programname.Replace("-", "_");
                    XMLOperations_safety.ModifyProgramName(ref newProgram, newProgram.Name);

                    newProgram.pouType = "program";

                    List<string> mylogic = new List<string>();
                    SortedDictionary<int, int> IDS = new SortedDictionary<int, int>();
                    List<string> Library = new List<string>();
                    IDS.Add(200, 200);
                    TXTProgramOperations_Safety sf = new TXTProgramOperations_Safety();
                    Dictionary<string, SafetyTemplate> newSafetyTemplateMap = sf.DeserializeSeminesSafetyTemplate(loadedXml, newProgram.Name, ref mylogic, ref IDS, ref Library, MyDB);
                    CreatelogicFileText(newSafetyTemplateMap, outputfil);
                    CreateLibraryandAdd(Library, ref XMLOperations_LadderLogic.blankPT2);
                    CreateSTSettinandAdd(ref XMLOperations_LadderLogic.blankPT2);

                    ADDSafetyXmlToProgram(newSafetyTemplateMap, ref newProgram, ref variableList, MyDB);//need to study

                    XElement myproxml = newProgram.MyXML;

                    XMLOperations_LadderLogic.BuildCoillogic(ref newSafetyTemplateMap, ref IDS, ref myproxml, mylogic);
                    CreatNewPrograms(newSafetyTemplateMap, ref variableList, MyDB);

                    newProgram.MyLogicList = mylogic;
                    newProgram.IDsMap = IDS;

                    newPrograms.Add(newProgram);

                    outputfil = seTfbfolder(OrginalXMLFileName);
                    SetAndSelectOutXML(TXTProgramOperations_Safety.TemplateXMLFile, XMLOperations_LadderLogic.blankPT2, outputfil);
                    //  XMLOperations_LadderLogic.blankPT2.Save(outputfil);



                }
            }
            //}

            return newPrograms;

        }
        //seimens


    }


}
