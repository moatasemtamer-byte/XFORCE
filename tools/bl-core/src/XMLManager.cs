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

using BL.SymbolData;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BL
{
    public class XMLManager
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

        public static Dictionary<string, inOutVariable> globalVariableMap;


        








        public XMLManager()
        {
            RulebookName = string.Empty;
            DumpExcelName = string.Empty;
            NewXMLFileName = string.Empty;
            OrginalXMLFileName = string.Empty;
            Prs = new List<program>();
  
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

            BL.Logger.LogInfo(null, Status);

            Status = "ExitCode: " + ExitCode.ToString() + "ExecuteCommand";

            BL.Logger.LogInfo(null, Status);
            process.Close();
            return returnValue;
        }

        public void AddSafetyTEmplateToProgram(BL.SafetyData.SafetyTemplate AddedSafetyTemplate, ref XElement PrgInioFlgList)
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

            foreach (SafetyBlock block in AddedSafetyTemplate.SafetyBlockMap.Values)
            {
                FBDList.Add(block.MyXML);
            }

            foreach (inOutVariable vrable in AddedSafetyTemplate.inOutVariableMap.Values)
            {
                string newaddedvrble = vrable.expressionMap.Tagname;
                string newaddedvrbleXML = "variable name=" + "\"" + newaddedvrble + "\"";
                FBDList.Add(vrable.MyXML);
                if ((vrable.VariableType == "Local_Varaible") && (!LOCvariableList.ToString().Contains(newaddedvrbleXML)))
                {
                    LOCvariableList.Add(vrable.MyInterfaceXML);
                }

                if ((vrable.VariableType == "Global_Variable") && (!globalvariableList.ToString().Contains(newaddedvrbleXML)))
                {
                    globalvariableList.Add(vrable.MyInterfaceXML);
                }
            }
        }



        public XMLTemplate GetSafetyTemplate(string ProgramName, string tagName)
        {
            XMLTemplate retval = null;
            if (Prs != null)
                retval = Prs.FirstOrDefault(pr => pr.Name == ProgramName).SafetyTemplateMap[tagName];

   

            return retval;
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

        private void SearchIDinComment(ref SafetyTemplate modSafetyTemplate, string oldID, string newTagID)
        {
            

            Comment mycomment = modSafetyTemplate.CommentMap.FirstOrDefault(cmnt => cmnt.Value.localId == oldID).Value;
          
            ModifyCommentID(ref mycomment, newTagID);
        }

       public static void ModifyCommentID(ref Comment mycomment, string newTagID )
        {
            XElement commentXml = null;
            if (mycomment != null)
            {
                commentXml = mycomment.MyXML;
               
            }

            if (commentXml != null)
            {
                commentXml.Attribute("localId").Value = newTagID;

                mycomment.localId = newTagID;
            }
        }








   




    }
}