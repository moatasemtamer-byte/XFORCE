/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 *
 * Purpose:     Loads data sets from database
 *
 * Rev  Date         By         Purpose
 * ---  -------     -------     ---------------------------------------------------------------------
 * 
 ********************************************************************************************/

using Microsoft.Win32;
using OfficeOpenXml;

using BL.SafetyData;


using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
//using Excel = Microsoft.Office.Interop.Excel;

namespace BL
{
    public class TXTManager
    {
      

        public List<program> Prs;
      

        private static XElement _Database;
        private static XElement _OrginalDatabase;
     

        public ExcelPackage DumpXML;
        public DataTable SymbolProperties;
        public string RulebookName;
        public string OrginalXMLFileName;
        public string OrginalPT2FileName;
        public string DumpExcelName;

        public string NewXMLFileName;
        public string SafetyAPIName { get; set; }
        private string[] _DatabaseLines;
        public static Dictionary<string, inOutVariable> globalVariableMap;

        public TXTManager(string FileName, string FileType)
        {
            string[] root = null;
            if (FileType == FileWorks.LADDER)
            {
                if (LadderTextOperations_safety.MyDB != null)
                {    //  root = FileOperations.LoadXMLFile(FileName);
                    string status = "Read the LPT file in progress please wait... ";
                    BL.Logger.LogText(null, status);
                    root = File.ReadAllLines(FileName, Encoding.UTF8);
                    OrginalXMLFileName = FileName;
                    Intialization(root, FileName, FileType);
                }
                else
                {
                    string status = "Please drag/select the DPT file first before the LPT file";
                    BL.Logger.LogError(null, status);
                }
            }
            else
            {
                OrginalXMLFileName = FileName;
                LadderTextOperations_safety.MyDB = ReadDPT(FileName);
            }
        }

        public TXTManager(string FileName, string FileType, string APIName)
        {
            XElement root = null;
            SafetyAPIName = APIName;
            if (FileType != FileWorks.FOLDER && FileType != FileWorks.PT2_FILE)
            {
                root = FileWorks.LoadXMLFile(FileName);
                OrginalXMLFileName = FileName;
                Intialization(root, FileName, FileType);
            }
            if (FileType == FileWorks.PT2_FILE)
            {
                if (APIName != string.Empty)
                {
                    OrginalXMLFileName = FileName;
                    OrginalPT2FileName = FileName;
                    string outputXmlFile = FileName.ToUpper().Replace(".PT2", ".XML");

                    string Status = "Exporting The PT2 Data In Progress Please wait: ";
                    BL.Logger.LogText(null, Status);

                    if (GenerateImportExportCmd(outputXmlFile, FileName, "-export"))
                    {
                        OrginalXMLFileName = outputXmlFile;
                    
                        OrginalPT2FileName = FileName;
                        root = FileWorks.LoadXMLFile(outputXmlFile);
                        FileName = OrginalXMLFileName;
                        FileType = FileWorks.SAFETY_XML_FILE;

                        Intialization(root, FileName, FileType);
                    }
                }
            }
        }

        private bool Intialization(XElement database, string FileName, string FileType)
        {
            bool returnval = false;

            switch (FileType)
            {
                case FileWorks.SAFETY_XML_FILE:
                    Prs = XMLOperations_safety.DeserializeSafetyProgram(database);
                    globalVariableMap = XMLOperations_safety.globalVariableMap;
                    returnval = true;
                    break;



            }

            if (returnval)
            {
                //_ds_GElement = new DS_GElement(this);
                //_ds_SafetyFolder = new DS_SafetyFolder(this);
                _Database = database;

                // _ds_SafetyProperty = new DS_SafetyPropertyGroup(this);
            }
            return returnval;
        }

        private bool Intialization(string[] database, string FileName, string FileType)
        {
            bool returnval = false;
            string outputfile = "";
            switch (FileType)
            {
                case FileWorks.LADDER:
                    LadderTextOperations_safety ld = new LadderTextOperations_safety();
                    Prs = ld.DeserializeSafetyProgram(database, ref outputfile, OrginalXMLFileName);
                    CreateMismatchLogfile(Prs.FirstOrDefault().MyLogicList);
                    globalVariableMap = LadderTextOperations_safety.globalVariableMap;
                    convertladderToFBPT2(outputfile);
                    returnval = true;
                    break;
            }

            if (returnval)
            {
                //_ds_GElement = new DS_GElement(this);
                //_ds_SafetyFolder = new DS_SafetyFolder(this);
                _DatabaseLines = database;

                // _ds_SafetyProperty = new DS_SafetyPropertyGroup(this);
            }
            return returnval;
        }

        public XElement Database
        {
            get
            {
                return _Database;
            }
            set
            {
                _Database = value;
                _OrginalDatabase = new XElement(value);
            }
        }




        public void CreateMismatchLogfile(List<string> mismatches)
        {
            mismatches = mismatches.Where(x => x.Contains("Network #")).ToList();
            if (mismatches.Count > 0)
            {


                string fullPath = Path.GetDirectoryName(OrginalXMLFileName);
                string OrginalXMLwithoutExt = System.IO.Path.GetFileNameWithoutExtension(OrginalXMLFileName);

                fullPath =  System.IO.Path.Combine(fullPath, "FB" + "_" + OrginalXMLwithoutExt);

                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }

                string outputLogFile = System.IO.Path.Combine(fullPath, OrginalXMLwithoutExt + "_" + Properties.Resources.BL_ReplicationRulebookLogFileName);
                System.IO.File.WriteAllLines(outputLogFile, mismatches.ToArray());
                string Status = "log file Created on Path:" + outputLogFile;

                BL.Logger.LogInfo(null, Status);
            }
        }

        public void CreateHistoryLogfile(List<string> mismatches)
        {
            mismatches = mismatches.Where(x => x.Contains("Network #")).ToList();
            if (mismatches.Count > 0)
            {


                string fullPath = Path.GetDirectoryName(OrginalXMLFileName);
                string OrginalXMLwithoutExt = System.IO.Path.GetFileNameWithoutExtension(OrginalXMLFileName);

                fullPath = System.IO.Path.Combine(fullPath, "FB" + "_" + OrginalXMLwithoutExt);

                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }

                string outputLogFile = System.IO.Path.Combine(fullPath, OrginalXMLwithoutExt + "_" + "HistoryLog.txt");
                System.IO.File.WriteAllLines(outputLogFile, mismatches.ToArray());
                string Status = "History file Created on Path:" + outputLogFile;

                BL.Logger.LogInfo(null, Status);
            }
        }

        public TXTManager()
        {
            RulebookName = string.Empty;
            DumpExcelName = string.Empty;
            NewXMLFileName = string.Empty;
            OrginalXMLFileName = string.Empty;
            Prs = new List<program>();
            //_ds_GElement = new DS_GElement(this);
            //_ds_SafetyFolder = new DS_SafetyFolder(this);

            //_ds_SafetyProperty = new DS_SafetyPropertyGroup(this);
        }

        private bool GenerateImportExportCmd(string XMLFILENAME, string PT2FILENAME, string command)
        {
            int ExitCode;
            bool returnValue = true;
            string TristationPath = SafetyAPIName;
            ProcessStartInfo ProcessInfo = new ProcessStartInfo(TristationPath, command + @" " + @"""" + XMLFILENAME + @"""" + " " + @"""" + PT2FILENAME + @"""");

            Process process = new Process();

            ProcessInfo.CreateNoWindow = true;
            ProcessInfo.UseShellExecute = false;
            ProcessInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(TristationPath);
            // *** Redirect the output ***
            ProcessInfo.RedirectStandardError = true;
            ProcessInfo.RedirectStandardOutput = true;

            using (new WaitCursor())
            {
                process = Process.Start(ProcessInfo);
                process.WaitForExit();
            }

            // *** Read the streams ***
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!File.Exists(XMLFILENAME) || !File.Exists(PT2FILENAME))
            {
                output = output + "Fail";
            }
            if (output.Contains("Fail"))
                returnValue = false;
            ExitCode = process.ExitCode;

            string Status = "error>>" + (String.IsNullOrEmpty(error) ? "(none)" : error);

            BL.Logger.LogText(null, Status);

            Status = "ExitCode: " + ExitCode.ToString() + "ExecuteCommand";

           // BL.Logger.LogInfo(null, Status);
            process.Close();
            return returnValue;
        }

        public bool convertladderToFBPT2(string XMLFILENAME)
        {
            bool returnvalue = false;
            Dictionary<string, string> installedApi = TXTManager.PrepareAPISeletionDictionary();
            //var API15 = installedApi.FirstOrDefault(x => x.Value.Contains("15")).Key;
          var  API15 = installedApi.FirstOrDefault(x => x.Value.Contains("17")).Key;
            string fullPath, OrginalXMLwithoutExt;
            seTfbfolder(out fullPath, out OrginalXMLwithoutExt);
            if (!string.IsNullOrEmpty(API15))
            {
                string Status = "Importing new Data in The PT2 Data In Progress Please wait: ";
                BL.Logger.LogText(null, Status);

                string outputPT2File = fullPath + "\\" + OrginalXMLwithoutExt + "_FB.PT2";

                SafetyAPIName = API15;
                if (GenerateImportExportCmd(XMLFILENAME, outputPT2File, "-import"))
                {
                    Status = "Importing data into PT2 Succeeded output Files Created on Path:" + fullPath;
                    BL.Logger.LogInfo(null, Status);
                }
                else
                {
                    Status = Properties.Resources.BL_ReplicationSuccessbutAPIFailMsg + fullPath;
                    BL.Logger.LogInfo(null, Status);
                }
            }
            else
            {
                BL.Logger.LogInfo(null, "Please install API ver 4.15 to convert the XML to PT2");
                BL.Logger.LogInfo(null, "the XML file has been created on the following Path:" + fullPath);
            }
            return returnvalue;
        }

        private void seTfbfolder(out string fullPath, out string OrginalXMLwithoutExt)
        {
            fullPath = Path.GetDirectoryName(OrginalXMLFileName);
            OrginalXMLwithoutExt = System.IO.Path.GetFileNameWithoutExtension(OrginalXMLFileName);
            //string RulebookNamewithoutExt = System.IO.Path.GetFileNameWithoutExtension(RulebookName);
            fullPath = System.IO.Path.Combine(fullPath, "FB" + "_" + OrginalXMLwithoutExt);

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        public  void AddSafetyTEmplateToProgram(BL.SafetyData.SafetyTemplate AddedSafetyTemplate, ref XElement PrgInioFlgList, ref XElement myconfiguration, DataTable MyDB,bool forceAdd)
        {
            var LOCvariableList = (from HH in PrgInioFlgList.Descendants()
                                   where HH.Name.LocalName == "localVars" && !(HH.ToString().Contains("Constants"))
                                   select HH).FirstOrDefault();

            var globalvariableList = (from HH in PrgInioFlgList.Descendants()
                                      where HH.Name.LocalName == "globalVars" && !(HH.ToString().Contains("Constants"))
                                      select HH).FirstOrDefault();

            var FBDList = (from d in PrgInioFlgList.Descendants()
                           where d.Name.LocalName == "FBD"
                           select d).FirstOrDefault();

            var sheetList = (from HH in FBDList.Descendants()
                             where HH.Name.LocalName == "SHEETS"
                             select HH).FirstOrDefault();
            sheetList.Add(AddedSafetyTemplate.MyXMLSheet);

            foreach (Comment comnt in AddedSafetyTemplate.CommentMap.Values)
            {
                FBDList.Add(comnt.MyXML);
            }
            if (AddedSafetyTemplate.Name != null && ((AddedSafetyTemplate.Name.ToUpper().Contains("PACK") && int.Parse(AddedSafetyTemplate.SheetNumber) >= 5)|| AddedSafetyTemplate.Name.ToUpper().Contains("TEMPLATE")|| forceAdd))
            {
                foreach (SafetyBlock block in AddedSafetyTemplate.SafetyBlockMap.Values)
                {
                    FBDList.Add(block.MyXML);
                }

                foreach (inOutVariable vrable in AddedSafetyTemplate.inOutVariableMap.Values)
                {
                    string newaddedvrble = vrable.expressionMap.Tagname;
                    string descreption = "";
                    string vrbtype = "";
                
                    string newaddedvrbleXML = "variable name=" + "\"" + newaddedvrble + "\"";
                    FBDList.Add(vrable.MyXML);
                    // if ((vrable.VariableType == "Local_Varaible") && (!LOCvariableList.ToString().Contains(newaddedvrbleXML)))
                    if ((vrable.VariableType == "Local_Varaible"))
                    {
                        LOCvariableList.Add(vrable.MyInterfaceXML);
                    }

                    // if ((vrable.VariableType == "Global_Variable") && (!globalvariableList.ToString().Contains(newaddedvrbleXML)))
                    if ((vrable.VariableType == "Global_Variable"))
                    {
                        globalvariableList.Add(vrable.MyInterfaceXML);
                    }
                }
                AddconstanttoProgram(AddedSafetyTemplate, ref PrgInioFlgList);
            }

            foreach (coil vrable in AddedSafetyTemplate.SafetycoilMap.Values)
            {
                coil myvarable = vrable;
                // GetTagType( MyDB, ref myvarable);
                string newaddedvrble = vrable.Name;
                string newaddedvrbleXML = "variable name=" + "\"" + newaddedvrble + "\"";
                XElement myxml = vrable.MyXML;
                string tag = vrable.Name;
                CheckHWTagsForAnnotation(MyDB, ref myxml, tag);
                FBDList.Add(vrable.MyXML);
               
                 
                //if ((vrable.VariableType == "Local_Varaible") && (!LOCvariableList.ToString().Contains(newaddedvrbleXML)))
                //{
                //    LOCvariableList.Add(vrable.MyInterfaceXML);
                //}

                //if ( (!globalvariableList.ToString().Contains(newaddedvrbleXML)))
                //{
                //    globalvariableList.Add(vrable.MyInterfaceXML);
                //}

                var newaddedvrbleXMLglbal = (from P in globalvariableList.Descendants()
                                             where P.Name.LocalName == "variable" && P.Attribute("name").Value == newaddedvrble
                                             select P).ToList();

                if (newaddedvrbleXMLglbal != null && newaddedvrbleXMLglbal.Count() == 0)
                {
                    globalvariableList.Add(vrable.MyInterfaceXML);
                }
                var newaddedvrbleXMLconf = (from P in myconfiguration.Descendants()
                                            where P.Name.LocalName == "variable" && P.Attribute("name").Value == newaddedvrble
                                            select P).ToList();

                if (newaddedvrbleXMLconf != null && newaddedvrbleXMLconf.Count() == 0)
                {
                    myconfiguration.Add(vrable.MyGlobalConfigXML);
                }

                //if ( (!myconfiguration.ToString().Contains(newaddedvrbleXML)))
                //{
                //    myconfiguration.Add(vrable.MyGlobalConfigXML);
                //}
            }

            foreach (contact vrable in AddedSafetyTemplate.SafetycontactMap.Values.Where(x => !string.IsNullOrEmpty(x.Name)))
            {
                string newaddedvrble = vrable.Name;
                string newaddedvrbleXML = "variable name=" + "\"" + newaddedvrble + "\"";
                XElement myxml = vrable.MyXML;
                string tag = vrable.Name;
                CheckHWTagsForAnnotation(MyDB, ref myxml, tag);
                FBDList.Add(vrable.MyXML);
                //if ((vrable.VariableType == "Local_Varaible") && (!LOCvariableList.ToString().Contains(newaddedvrbleXML)))
                //{
                //    LOCvariableList.Add(vrable.MyInterfaceXML);
                //}

                //if ((!globalvariableList.ToString().Contains(newaddedvrbleXML)))
                //{
                //    globalvariableList.Add(vrable.MyInterfaceXML);
                //}

                //if ((!myconfiguration.ToString().Contains(newaddedvrbleXML)))
                //{
                //    myconfiguration.Add(vrable.MyGlobalConfigXML);
                //}
                var newaddedvrbleXMLglbal = (from P in globalvariableList.Descendants()
                                             where P.Name.LocalName == "variable" && P.Attribute("name").Value == newaddedvrble
                                             select P).ToList();

                if (newaddedvrbleXMLglbal != null && newaddedvrbleXMLglbal.Count() == 0)
                {
                    globalvariableList.Add(vrable.MyInterfaceXML);
                }
                var newaddedvrbleXMLconf = (from P in myconfiguration.Descendants()
                                            where P.Name.LocalName == "variable" && P.Attribute("name").Value == newaddedvrble
                                            select P).ToList();

                if (newaddedvrbleXMLconf != null && newaddedvrbleXMLconf.Count() == 0)
                {
                    myconfiguration.Add(vrable.MyGlobalConfigXML);
                }
            }
        }

        private XElement CheckHWTagsForAnnotation(DataTable MyDB,ref XElement myxml, string tag)
        {
           try
            {
                string mytype = (from DataRow dr in MyDB.Rows
                                 where (string)dr["TagName"].ToString().ToUpper() == tag.ToUpper()
                                 select (string)dr["POINTTYPE"]).FirstOrDefault();
                string alias = (from DataRow dr in MyDB.Rows
                                where (string)dr["TagName"].ToString().ToUpper() == tag.ToUpper()
                                select (string)dr["ALIASTYPE"]).FirstOrDefault();

                if (mytype == "O" || mytype == "I" || alias != "U")
                {
                    ChangeAnnotation(ref myxml);
                }
            }
            catch(Exception tt)
            {
                string msg = tt.Message;
            }
               
          
            

            return myxml;
        }

        private void ChangeAnnotation(ref XElement myXML)
        {
            var newaddedvrbleXMLglbal = (from P in myXML.Descendants()
                                         where P.Name.LocalName == "TEXT"
                                         select P).FirstOrDefault();
            //newaddedvrbleXMLglbal.Value = "%P1"; NESMA
            newaddedvrbleXMLglbal.Value = "%P0";
        }

        private  void AddconstanttoProgram(SafetyTemplate safetyTemplate, ref XElement myprogamxml)
        {

            var globalvariableList = (from HH in myprogamxml.Descendants()
                                      where HH.Name.LocalName == "localVars" && (HH.ToString().Contains("Constants"))
                                      select HH).FirstOrDefault();


            foreach (inOutVariable vrable in safetyTemplate.inOutVariableMap.Values)
            {
                string newaddedvrble = vrable.expressionMap.Tagname;
                string newaddedvrbleXML = "variable name=" + "\"" + newaddedvrble + "\"";
                if ((vrable.VariableType == "Constant") && (!globalvariableList.ToString().Contains(newaddedvrbleXML)))
                {

                    globalvariableList.Add(vrable.MyInterfaceXML);
                }
            }

        }

        private static void GetTagType( DataTable myDB, ref coil myvarable)
        {
            string tag = myvarable.Name.ToUpper();
          

            var tagInfo = (from p in myDB.AsEnumerable()
                           where p.Field<string>("TagName").ToUpper().Trim() == tag
                           select new
                           {
                               dataType = p.Field<string>("datatype"),
                               descrption = p.Field<string>("Description"),
                               aliastype = p.Field<string>("ALIASTYPE"),
                               pointtype = p.Field<string>("POINTTYPE")
                           }).FirstOrDefault();

            string modType = "";
            string mytype = tagInfo.dataType;
            if (!string.IsNullOrEmpty(mytype))
            {
                switch (mytype)
                {
                    case "D":
                        modType =  "BOOL";
                        break;

                    case "R":
                        modType = "REAL";
                        break;

                    case "I":
                        modType = "DINT";
                        break;
                }
            }
            if(!string.IsNullOrEmpty(modType))
            {
               ChangeCoilType(ref myvarable, modType);
            }
            else
            {

            }
           
            ChangeCoilDescription(ref myvarable, tagInfo.descrption);

        }

        private static void ChangeCoilDescription(ref coil myvarable, string descrption)
        {
            // throw new NotImplementedException();
            XElement ioVariable = myvarable.MyXML;
            XElement ioInterfaceVariable = myvarable.MyInterfaceXML;
            XElement ioGlobalConfigVariable = myvarable.MyGlobalConfigXML;

            if (ioInterfaceVariable != null)
            {
                var LOCdiscp = ioInterfaceVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                if (LOCdiscp != null)
                {
                    LOCdiscp.ReplaceNodes(new XCData(descrption));

                    myvarable.BlockAttributes.DESCRIPTION = descrption;
                }
            }
            if (ioGlobalConfigVariable != null)
            {
                var discp = ioGlobalConfigVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "documentation");
                discp.ReplaceNodes(new XCData(descrption));

                myvarable.BlockAttributes.DESCRIPTION = descrption;
            }
        }

        private static void ChangeCoilType(ref coil myvarable, string modType)
        {
            XElement ioVariable = myvarable.MyXML;
            XElement ioInterfaceVariable = myvarable.MyInterfaceXML;
            XElement ioGlobalConfigVariable = myvarable.MyGlobalConfigXML;

            if (ioInterfaceVariable != null)
            {
                var LOCdiscp = ioInterfaceVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "type");
                if (LOCdiscp != null)
                {
                    // LOCdiscp.SetElementValue= new XElement("modType"); ;
                    XNamespace  xmlns = "http://www.plcopen.org/xml/tc6.xsd";
                    LOCdiscp.ReplaceNodes(new XElement(xmlns+modType)); 

                    myvarable.BlockAttributes.DATATYPE = modType;
                }
            }
            if (ioGlobalConfigVariable != null)
            {
                var discp = ioGlobalConfigVariable.Descendants().FirstOrDefault(XML => XML.Name.LocalName == "type");
                XNamespace xmlns = "http://www.plcopen.org/xml/tc6.xsd";
                discp.ReplaceNodes(new XElement(xmlns + modType));

                myvarable.BlockAttributes.DATATYPE = modType;
            }
        }



        public SafetyTemplate DuplicateSafetyTemplate(SafetyTemplate modSafetytemplate, SortedDictionary<int, int> IDS)
        {
            foreach (inOutVariable inoutvar in modSafetytemplate.inOutVariableMap.Values)
            {
                inOutVariable testinoutvar = inoutvar;
                string oldID = testinoutvar.localId;

                string newID = (IDS.ElementAt(IDS.Count() - 1).Value + 1).ToString();
                IDS.Add(int.Parse(newID), int.Parse(newID));

                SearchIDinIOvariable(ref modSafetytemplate, oldID, newID);
            }

            foreach (SafetyBlock blk in modSafetytemplate.SafetyBlockMap.Values)
            {
                string oldID = blk.localId;

                string newID = (IDS.ElementAt(IDS.Count() - 1).Value + 1).ToString();
                IDS.Add(int.Parse(newID), int.Parse(newID));

                SearchIDinBlocks(ref modSafetytemplate, oldID, newID);
            }

            foreach (Comment commnt in modSafetytemplate.CommentMap.Values)
            {
                string oldID = commnt.localId;

                string newID = (IDS.ElementAt(IDS.Count() - 1).Value + 1).ToString();
                IDS.Add(int.Parse(newID), int.Parse(newID));

                SearchIDinComment(ref modSafetytemplate, oldID, newID);
            }
            return modSafetytemplate;
        }

        private void SearchIDinIOvariable(ref SafetyTemplate modSafetyTemplate, string oldID, string newTagID)
        {
            XElement ioVariableXml = null;

            var ioVariablevalue = modSafetyTemplate.inOutVariableMap.FirstOrDefault(vr => vr.Value.localId == oldID).Value;
            if (ioVariablevalue != null)
            {
                ioVariableXml = ioVariablevalue.MyXML;
            }
            inOutVariable ioVariable = modSafetyTemplate.inOutVariableMap.FirstOrDefault(vr => vr.Value.localId == oldID).Value;

            if (ioVariableXml != null)
            {
                ioVariableXml.Attribute("localId").Value = newTagID;

                ioVariable.localId = newTagID;
                foreach (SafetyBlock bloks in modSafetyTemplate.SafetyBlockMap.Values)
                {
                    if (bloks.MyXML.ToString().Contains(oldID))
                    {
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
        }

        private void SearchIDinBlocks(ref SafetyTemplate modSafetyTemplate, string oldID, string newTagID)
        {
            XElement BlockXml = null;

            var Blockvalue = modSafetyTemplate.SafetyBlockMap.FirstOrDefault(blk => blk.Value.localId == oldID).Value;
            SafetyBlock myBlock;
            if (Blockvalue != null)
            {
                BlockXml = Blockvalue.MyXML;
                myBlock = modSafetyTemplate.SafetyBlockMap.FirstOrDefault(blk => blk.Value.localId == oldID).Value;
            }

            if (BlockXml != null)
            {
                BlockXml.Attribute("localId").Value = newTagID;

                Blockvalue.localId = newTagID;

                foreach (var invar in Blockvalue.inputVariablesMap.variableMap)
                {
                    invar.Value.fatherblockID = newTagID;
                }

                foreach (var outvar in Blockvalue.outputVariablesMap.variableMap)
                {
                    outvar.Value.fatherblockID = newTagID;
                }

                foreach (inOutVariable myvar in modSafetyTemplate.inOutVariableMap.Values)
                {
                    if (myvar.MyXML.ToString().Contains(oldID))
                    {
                        if (myvar.connectionMap.refLocalId == oldID)
                        {
                            myvar.connectionMap.refLocalId = newTagID;
                            myvar.connectionMap.MyXML.Attribute("refLocalId").Value = newTagID;
                        }
                    }
                }

                foreach (SafetyBlock bloks in modSafetyTemplate.SafetyBlockMap.Values)
                {
                    if (bloks.MyXML.ToString().Contains(oldID))
                    {
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
        }

        public void SearchIDinComment(ref SafetyTemplate modSafetyTemplate, string oldID, string newTagID)
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





      

        public static Dictionary<string, string> PrepareAPISeletionDictionary()
        {
            Dictionary<string, string> returnValues = new Dictionary<string, string>();
            List<string> APIs = TXTManager.GetApi_batfile("XmlImpExp");
            foreach (string Api in APIs)
            {
                string display = Regex.Split(Api, @"\\").Last();
                returnValues.Add(Api, display);
            }
            return returnValues;
        }

        private static List<string> GetApi_batfile(string ExeName)
        {
            // TriStation lives in the Windows registry; off Windows there is nothing to discover.
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return new List<string>();
            }

            //try
            //{
            //RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs");
            
            

            var subKey = OpenSubKey64();
            if (subKey == null) { subKey = OpenSubKey32(); }
            //    List<string> regfilepath = new List<string>();
            //    if (key != null) // Make sure there are Assemblies
            //    {
            //        foreach (string Keyname in key.GetValueNames())
            //        {
            //            if (Keyname.IndexOf(ExeName) > 0 && Keyname.Contains("bat"))
            //            {
            //                regfilepath.Add(Keyname);
            //            }
            //        }
            //    }
            //    return regfilepath;
            //}
            //catch (Exception ex)
            //{
            //    return null;
            //}
            return APIfiles(GetPathsForTristationVersions(subKey));
        }

        /// <summary>
        /// Get the API bat file path from Installed Tristation folders
        /// </summary>
        /// <param name="paths">Paths for installed Tristation folders</param>
        private static List<string> APIfiles(List<string> paths)
        {
            var fileArray = new List<string>();
            foreach (string onepath in paths)
            {
                if (Directory.Exists(onepath))
                {
                    var bat = Directory.GetFiles(onepath, "XmlImpExp*.bat").ToList().FirstOrDefault();
                    if (!string.IsNullOrEmpty(bat)) { fileArray.Add(bat); }
                }

            }
            return fileArray;
        }
        private static List<string> GetPathsForTristationVersions(RegistryKey subKey) => subKey.GetValueNames().Where(kv => kv.Contains("Triconex")
                                                                                                                              && kv.ToUpper().Contains("TRISTATION")
                                                                                                                              && kv.ToUpper().Contains("PROGRAMS")).ToList();

        /// <summary>
        /// Open installed 64 bit Softwares on current machine
        /// </summary>
        private static RegistryKey OpenSubKey64() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders");

        /// <summary>
        /// Open installed 32 bit Softwares on current machine
        /// </summary>
        private static  RegistryKey OpenSubKey32() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders");


        /// <summary>
        /// Open installed 64 bit Softwares on current machine
        public static void Log(BL.LoggerKind logtype, string Message)
        {
            switch (logtype)
            {
                case BL.LoggerKind.Error:
                    BL.Logger.LogError(null, Message);

                    break;

                case BL.LoggerKind.Text:

                    BL.Logger.LogText(null, Message);

                    break;

                case BL.LoggerKind.Warning:
                    BL.Logger.LogWarning(null, Message);

                    break;

                default:
                    break;
            }
        }

        public DataTable ReadDPT(string DPTPath)
        {
            // Read the file and display it line by line.
            DataTable DBDataTable = DoPT2ExcelFormat();
            // Read DPT File

            StreamReader DPTFile = new StreamReader(DPTPath);
            String NewLine;
            NewLine = "Empty";
            while (!NewLine.Contains("Description"))
            {
                NewLine = DPTFile.ReadLine().ToString();
            }
            int TagPos, DescrPos, GroupPos, ClassPos, TypePos;
            TagPos = NewLine.IndexOf("Tag");
            DescrPos = NewLine.IndexOf("Descr");
            GroupPos = NewLine.IndexOf("Group");
            ClassPos = NewLine.IndexOf("Class");
            TypePos = NewLine.IndexOf("Type");

            while (DPTFile.Peek() > 0)
            {
                NewLine = DPTFile.ReadLine().ToString();
                if (NewLine.Contains("Dictionary Entries") || NewLine.Contains("Var/Cons") || NewLine.Contains("Value @ Powerup") || NewLine.StartsWith(" ") || NewLine.Length < 3)
                {
                    // Non Data Row
                }
                else
                {
                    DataRow NewTag = DBDataTable.NewRow();
                    DBDataTable.Rows.Add(NewTag);

                    NewTag.SetField(0, NewLine.Substring(0, TagPos).Replace(" ", "").Replace("-", "_").Replace("!", "TRP"));
                    NewTag.SetField(1, NewLine.Substring(DescrPos, GroupPos - DescrPos).Replace("  ", ""));
                    NewTag.SetField(2, NewLine.Substring(GroupPos, ClassPos - GroupPos).Replace("  ", ""));
                    NewTag.SetField(3, NewLine.Substring(TagPos, DescrPos - TagPos).Replace("  ", ""));
                    NewTag.SetField(4, "U");
                    NewTag.SetField(7, "M");
                    NewTag.SetField(16, "S");
                    switch (NewLine.Substring(TypePos, 4))
                    {
                        case "DISC":
                            NewTag.SetField(6, "D");
                            break;

                        case "REAL":
                            NewTag.SetField(6, "R");
                            break;

                        case "INTE":
                            NewTag.SetField(6, "I");
                            break;
                    }

                }
            }
            // If you want to see the output DataTable
            DPTFile.Close();
            ExportToPT2(DBDataTable);
           DataTable myhw= ReadDPTWithHW(DPTPath);
            InsertTheMappingData(myhw);
            //return DBDataTable;
            return myhw;
        }

        private void InsertTheMappingData(DataTable myhw)
        {
            myhw.Columns.Add("FirstLetters");
            myhw.Columns.Add("Numbers");
            myhw.Columns.Add("middileLetters");
            myhw.Columns.Add("LastNumbers");
            myhw.Columns.Add("Suffix");
            myhw.Columns.Add("HWTag");
            foreach (DataRow row in myhw.Rows)
            {
                string tag=row["TagName"].ToString();

               
                var list = SplitAlpha(tag);
               
                PublishInTable(list, row);
               
            }
            foreach(DataRow row in myhw.Rows)
            {
                GetHWTagForMemory(row, myhw);
            }
            SaveMappingFile(myhw);
        }

        private void GetHWTagForMemory(DataRow row, DataTable myhw)
        {
            string firstletter = row["FirstLetters"].ToString().Substring(0,1);
            string numbers = row["Numbers"].ToString();
            string HWtag = GetHWTagFromDB(firstletter, numbers, myhw);
            row["HWTag"] = HWtag;
        }

        private string GetHWTagFromDB(string firstletter, string numbers, DataTable myhw)
        {
            string mytype = (from DataRow dr in myhw.Rows
                             where ((string)dr["TagName"].ToString().ToUpper().Substring(0,1) == firstletter.ToUpper()
                             && (string)dr["POINTTYPE"].ToString()=="O"|| (string)dr["POINTTYPE"].ToString() == "I" 
                             && (string)dr["Numbers"].ToString() == numbers)
                             select (string)dr["TagName"]).FirstOrDefault();
            if(string.IsNullOrEmpty(mytype))
            {
                mytype = (from DataRow dr in myhw.Rows
                          where ((string)dr["TagName"].ToString().ToUpper().Substring(0, 1) == firstletter.ToUpper()
                          && (string)dr["POINTTYPE"].ToString() == "O" || (string)dr["POINTTYPE"].ToString() == "I"
                          && dr["Numbers"].ToString().Contains(numbers))
                          select (string)dr["TagName"]).FirstOrDefault();
            }
            
            return mytype;
        }

        private void SaveMappingFile(DataTable myhw)
        {
            ExcelPackage pck = new ExcelPackage();
            ExcelWorksheet ws = pck.Workbook.Worksheets.Add("HWDBPT2");
            ws.Cells["A1"].LoadFromDataTable(myhw, true);
            string fullPath, OrginalXMLwithoutExt;
            seTfbfolder(out fullPath, out OrginalXMLwithoutExt);

            string Status = "generating the DB with HW  In Progress Please wait: ";
            BL.Logger.LogText(null, Status);

            string outputPT2File = fullPath + "\\" + OrginalXMLwithoutExt + "_HWDBMapping.xlsx";

            Status = "the HW DB has been generated in  the following Path:" + outputPT2File;
            BL.Logger.LogInfo(null, Status);
            // pck.SaveAs(new System.IO.FileInfo(outputPT2File));
            FileWorks.SaveExcelPackageinPath(pck, outputPT2File);

            Status = "Now you can drag/select the LPT file";
            BL.Logger.LogText(null, Status);
        }

        private void PublishInTable(IEnumerable<string> list, DataRow row)
        {
            for(int i=0;i<list.Count();i++)
            {
                if (i <= 5)
                {
                    row.SetField(row.Table.Columns.Count - 6 + i, list.ElementAt(i));
                }
            }
        }

        public  IEnumerable<string> SplitAlpha(string input)
        {
            var words = new List<string> { string.Empty };
            for (var i = 0; i < input.Length; i++)
            {
                words[words.Count - 1] += input[i];
                if (i + 1 < input.Length && char.IsLetter(input[i]) != char.IsLetter(input[i + 1]))
                {
                    words.Add(string.Empty);
                }
            }
            return words;
        }

        public DataTable ReadDPTWithHW(string DPTPath)
        {
            // Read the file and display it line by line.
            DataTable DBDataTable = DoPT2ExcelFormat();
            // Read DPT File

            StreamReader DPTFile = new StreamReader(DPTPath);
            String NewLine;
            NewLine = "Empty";
            while (!NewLine.Contains("Description"))
            {
                NewLine = DPTFile.ReadLine().ToString();
            }
            int TagPos, DescrPos, GroupPos, ClassPos, TypePos, AliasPos, RkPos, SlPos, PtPos, ValuePos, DPPos, MinSpanPos, MaxSpanPos, PowerupPos;
            TagPos = NewLine.IndexOf("Tag");
            DescrPos = NewLine.IndexOf("Descr");
            GroupPos = NewLine.IndexOf("Group");
            ClassPos = NewLine.IndexOf("Class");
            TypePos = NewLine.IndexOf("Type");
            AliasPos = NewLine.IndexOf("Alias");
            RkPos = NewLine.IndexOf("Rk");
            SlPos = NewLine.IndexOf("Sl");
            PtPos = NewLine.IndexOf("Pt");
            ValuePos= NewLine.IndexOf("Value");
            DPPos= NewLine.IndexOf("DP");
            MinSpanPos= NewLine.IndexOf("MinSpan");
            MaxSpanPos = NewLine.IndexOf("MaxSpan");
            PowerupPos= NewLine.IndexOf("Powerup");
            while (DPTFile.Peek() > 0)
            {
                NewLine = DPTFile.ReadLine().ToString();
                if (NewLine.Contains("Dictionary Entries") || NewLine.Contains("Var/Cons") || NewLine.Contains("Value @ Powerup") || NewLine.StartsWith(" ") || NewLine.Length < 3)
                {
                    // Non Data Row
                }
                else
                {
                    DataRow NewTag = DBDataTable.NewRow();
                    DBDataTable.Rows.Add(NewTag);

                    NewTag.SetField(0, NewLine.Substring(0, TagPos).Replace(" ", "").Replace("-", "_").Replace("!","TRP"));
                    NewTag.SetField(1, NewLine.Substring(DescrPos, GroupPos - DescrPos).Replace("  ", ""));
                    NewTag.SetField(2, NewLine.Substring(GroupPos, ClassPos - GroupPos).Replace("  ", ""));
                    NewTag.SetField(3, NewLine.Substring(TagPos, DescrPos - TagPos).Replace("  ", ""));
                    
                    if(NewLine.Contains("NONRETENTIVE"))
                    {
                        NewTag.SetField(9, "N");
                    }
                    else
                    {
                        if (NewLine.Contains("RETENTIVE"))
                        {
                            NewTag.SetField(9, "R");
                        }
                        else
                        {
                            NewTag.SetField(9, "N");
                        }
                    }
                    

                    try
                    {
                        if(NewLine.Length>= MinSpanPos)
                        {
                            NewTag.SetField(13, NewLine.Substring(MinSpanPos, MaxSpanPos - MinSpanPos).Replace(" ", ""));
                            NewTag.SetField(14, NewLine.Substring(MaxSpanPos, NewLine.Length - MaxSpanPos).Replace(" ", ""));
                            NewTag.SetField(15, NewLine.Substring(DPPos, MinSpanPos - DPPos).Replace(" ", ""));

                        }
                     
                    }
                    catch (Exception tt)
                    {

                        string g = tt.Message;
                    }
                    NewTag.SetField(4, "U");
                  //  NewTag.SetField(7, "M");
                    NewTag.SetField(16, "S");
                    switch (NewLine.Substring(TypePos, 4))
                    {
                        case "DISC":
                            NewTag.SetField(6, "D");
                            break;

                        case "REAL":
                            NewTag.SetField(6, "R");
                            break;

                        case "INTE":
                            NewTag.SetField(6, "I");
                            break;
                    }

                   string pttype = NewLine.Substring(ClassPos, TypePos - ClassPos).Replace(" ","");
                    int aliasnu = 0;
                    switch (pttype)
                    {
                        case "MEMORY":
                            NewTag.SetField(7, "M");
                            string alias = NewLine.Substring(TypePos+5, RkPos - (TypePos + 5)).Replace(" ", "");

                            if(alias!="" && alias!= "VARIABLE" && int.TryParse(alias, out aliasnu))
                            {
                                NewTag.SetField(5, alias);
                                if( (aliasnu >=12001 && aliasnu<=14000) ||(aliasnu >= 31001 && aliasnu <= 32000)|| (aliasnu >= 33001 && aliasnu <= 34000))
                                {
                                    NewTag.SetField(4, "R");
                                }
                                else
                                {
                                    NewTag.SetField(4, "W");
                                }

                            }
                            
                            break;

                        case "OUTPUT":
                            NewTag.SetField(7, "O");
                            NewTag.SetField(4, "W");
                            NewTag.SetField(10, NewLine.Substring(RkPos, SlPos - RkPos).Replace(" ", ""));
                            NewTag.SetField(11, NewLine.Substring(SlPos, PtPos - SlPos).Replace(" ", ""));
                            NewTag.SetField(12, NewLine.Substring(PtPos, ValuePos - PtPos).Replace(" ", ""));
                            break;

                        case "INPUT":
                            NewTag.SetField(7, "I");
                            NewTag.SetField(4, "R");
                            NewTag.SetField(10, NewLine.Substring(RkPos, SlPos - RkPos).Replace(" ", ""));
                            NewTag.SetField(11, NewLine.Substring(SlPos, PtPos - SlPos).Replace(" ", ""));
                            NewTag.SetField(12, NewLine.Substring(PtPos, ValuePos - PtPos).Replace(" ", ""));
                            break;
                    }
                   

                }
            }
            // If you want to see the output DataTable
            DPTFile.Close();
            ExportToPT2HW(DBDataTable);
            return DBDataTable;
        }

        private  DataTable DoPT2ExcelFormat()
        {
            DataTable DBDataTable = new DataTable();
            DBDataTable.Columns.Add("TagName");
            DBDataTable.Columns.Add("Description");
            DBDataTable.Columns.Add("Group1");
            DBDataTable.Columns.Add("Group2");
            DBDataTable.Columns.Add("ALIASTYPE");
            DBDataTable.Columns.Add("ALIASNUM");
            DBDataTable.Columns.Add("DATATYPE");
            DBDataTable.Columns.Add("POINTTYPE");
            DBDataTable.Columns.Add("INITVALUE");
            DBDataTable.Columns.Add("RETENTIVE");
            DBDataTable.Columns.Add("CHASSIS");
            DBDataTable.Columns.Add("SLOT");
            DBDataTable.Columns.Add("POINT");
            DBDataTable.Columns.Add("MINSPAN");
            DBDataTable.Columns.Add("MAXSPAN");
            DBDataTable.Columns.Add("DECPL");
            DBDataTable.Columns.Add("APPLICATION");
            return DBDataTable;
        }

        public void ExportToPT2(DataTable mydata)
        {
            ExcelPackage pck = new ExcelPackage();
            ExcelWorksheet ws = pck.Workbook.Worksheets.Add("DBPT2");
            ws.Cells["A1"].LoadFromDataTable(mydata, true);
            string fullPath, OrginalXMLwithoutExt;
            seTfbfolder(out fullPath, out OrginalXMLwithoutExt);

            string Status = "generating the DB  In Progress Please wait: ";
            BL.Logger.LogText(null, Status);

            string outputPT2File = fullPath + "\\" + OrginalXMLwithoutExt + "_DB.xlsx";

            Status = "the DB has been generated in  the following Path:" + outputPT2File;
            BL.Logger.LogInfo(null, Status);
           // pck.SaveAs(new System.IO.FileInfo(outputPT2File));
           FileWorks.SaveExcelPackageinPath(pck, outputPT2File);

            //Status = "Now you can drag/select the LPT file";
            //BL.Logger.LogText(null, Status);
        }

        public void ExportToPT2HW(DataTable mydata)
        {
            ExcelPackage pck = new ExcelPackage();
            ExcelWorksheet ws = pck.Workbook.Worksheets.Add("HWDBPT2");
            ws.Cells["A1"].LoadFromDataTable(mydata, true);
            string fullPath, OrginalXMLwithoutExt;
            seTfbfolder(out fullPath, out OrginalXMLwithoutExt);

            string Status = "generating the DB with HW  In Progress Please wait: ";
            BL.Logger.LogText(null, Status);

            string outputPT2File = fullPath + "\\" + OrginalXMLwithoutExt + "_HWDB.xlsx";

            Status = "the HW DB has been generated in  the following Path:" + outputPT2File;
            BL.Logger.LogInfo(null, Status);
            // pck.SaveAs(new System.IO.FileInfo(outputPT2File));
            FileWorks.SaveExcelPackageinPath(pck, outputPT2File);

            Status = "Now you can drag/select the LPT file";
            BL.Logger.LogText(null, Status);
        }

        //seimens
        public void ExcelManager(string FileName, string FileType)
        {
            string[] root = null;
            if (FileType == FileWorks.SiemensParser)
            {
                // if (LadderTextOperations_safety.MyDB != null)

                // if (LadderTextOperations_safety.MyDB == null)
                //{    //  root = FileOperations.LoadXMLFile(FileName);
                    string status = "Read the Excel parser file in progress please wait... ";
                    BL.Logger.LogText(null, status);
                    List<string> rootMismatches = new List<string>();
                   root = ReadSiemensParser(FileName,out rootMismatches);
                    OrginalXMLFileName = FileName;
                    IntializationSeimens(root, FileName, FileType, rootMismatches);
                //}
                //else
                //{
                  //  string status = "Please drag/select the DPT file first before the LPT file";
                    //BL.Logger.LogError(null, status);
                //}
            }
            else
            {
                OrginalXMLFileName = FileName;
                LadderTextOperations_safety.MyDB = ReadDPT(FileName);
            }
        }

        private string[] ReadSiemensParser(string fileName,out List<string> rootMismatches)
        {
            List<string> root = new List<string>();
             rootMismatches = new List<string>();
            var excel = FileWorks. OpenExcelPackage(fileName);
            if (excel != null) 
            {
                foreach (var sheet in excel.Workbook.Worksheets.Where(sh=>sh.Name.Contains("Function")) )
                {
               
                    for (int indexOfSheet=2; indexOfSheet<=sheet.Dimension.End.Row; indexOfSheet++)
                    {
                   
                        if (IsValidLogic(sheet.Cells[indexOfSheet, 1].Text, sheet.Cells[indexOfSheet, 3].Text))
                        {
                            
                            string logic = sheet.Cells[indexOfSheet, 1].Text + "@" + sheet.Cells[indexOfSheet, 2].Text + "@" + sheet.Cells[indexOfSheet, 3].Text
                            + "@" + sheet.Cells[indexOfSheet, 4].Text;
                            logic = logic.Trim().Replace(" (", "(").Replace(" =", "=").Replace("= ", "=");
                            root.Add(logic);
                        }
                        else
                        {
                            rootMismatches.Add("Network # " + sheet.Cells[indexOfSheet, 3].Text + " " + sheet.Cells[indexOfSheet, 2].Text + " can't be converted:this network need manual work");
                        }
                        
                    }
                }
            }
            return root.ToArray();
            
        }

        private bool IsValidLogic(string text,string network)
        {
            text=text.Trim();
            return (!text.StartsWith("=") && text.Contains("=") && (text.Contains("(") && text.Contains(")") && text.Split('=').Count() == 2) || (text.Split('=').Count() == 2 && !text.Contains("(")) && !text.Contains(":"));//&& !network.Contains("Unclear");
        }

        private bool IntializationSeimens(string[] database, string FileName, string FileType, List<string> rootMismatches)
        {
            bool returnval = false;
            string outputfile = "";
            switch (FileType)
            {
                case FileWorks.SiemensParser:
                    LadderTextOperations_safety ld = new LadderTextOperations_safety();
                    Prs = ld.DeserializeSafetyProgramSeimens(database, ref outputfile, OrginalXMLFileName);
                    List<string> AllMismatches=AddMismatches(Prs.FirstOrDefault().MyLogicList, rootMismatches);
                    // CreateMismatchLogfile(Prs.FirstOrDefault().MyLogicList);
                    CreateMismatchLogfile(AllMismatches);
                    globalVariableMap = LadderTextOperations_safety.globalVariableMap;
                    convertladderToFBPT2(outputfile);
                    returnval = true;
                    break;
            }

            if (returnval)
            {
                //_ds_GElement = new DS_GElement(this);
                //_ds_SafetyFolder = new DS_SafetyFolder(this);
                _DatabaseLines = database;

                // _ds_SafetyProperty = new DS_SafetyPropertyGroup(this);
            }
            return returnval;
        }

        private List<string> AddMismatches(List<string> myLogicList, List<string> rootMismatches)
        {
            myLogicList.AddRange(rootMismatches);
            return myLogicList;
        }
        //seimens
    }
}