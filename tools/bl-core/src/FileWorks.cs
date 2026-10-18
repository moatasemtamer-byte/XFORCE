using OfficeOpenXml;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Math;


/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 *
 * Purpose:             File operation .
 *
 *
 * Rev  DATE        Author   Description
 * ---  --------    ------   ---------------------------------------------------------

 * ********************************************************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BL
{
    public partial class FileWorks
    {
        public const string OTHER_FILE = "OTHER_FILE";
        public const string XML_FILE = "XML File";
        public const string EXCEL_FILE = "Excel File";
        public const string SAFETY_XML_FILE = "Safety XML File";
        public const string PT2_FILE = "PT2 FILE";
        public const string SCHNEIDER_XML_FILE = "Schnieder XML File";
        public const string CONTROL_XML_FILE = "Control XML File";
        public const string SIGNATURE_EXCEL_FILE = "Signature File";
        public const string FOLDER = "FOLDER";
        public const string LADDER = "LADDERDIAGRAM";
        public const string SiemensParser = "SiemensParser";




        public delegate void DragDropEventHandler(string FileName, string FileType);

        public static event DragDropEventHandler SafetyTemplate_DragDrop;

        public static string FileType(string FileName)
        {
            string returnText = OTHER_FILE;
            try
            {
                string extension = FileTypeExt(FileName);
                if (extension == XML_FILE)
                {
                    XElement XMLFile = null;

                    XMLFile = XElement.Load(FileName);

                    returnText = XMLFileType(XMLFile);
                }
                if (extension == EXCEL_FILE)
                {
                    returnText = EXCEL_FILE;
                    var excel = OpenExcelPackage(FileName);
                    if (excel != null && excel.Workbook.Worksheets.FirstOrDefault(sh=>sh.Name.Contains("Function") )!=null) { returnText = SiemensParser; }
                    //if (ValidateSignatureSheet(OpenExcelSheet(FileName, 1)))
                    //    returnText = SIGNATURE_EXCEL_FILE;
                }
                if (extension == FOLDER)
                {
                    returnText = FOLDER;
                }
                if (extension == PT2_FILE)
                {
                    returnText = PT2_FILE;
                }
                if (extension == LADDER)
                {
                    returnText = LADDER;
                }
                if (extension == LADDER + "DPT")
                {
                    returnText = LADDER + "DPT";
                }
            }
            catch (Exception ex)
            {
                returnText = Properties.Resources.BL_FileOperations_DragErrorMsg;
            }
            return returnText;
        }

     

        public static string XMLFileType(XElement XMLFile)
        {
            string returnText = OTHER_FILE;
            if (XMLFile != null)
            {
                if (Check_Safety_XML_File(XMLFile))
                    returnText = SAFETY_XML_FILE;

                if (Check_Schneider_XML_File(XMLFile))
                    returnText = SCHNEIDER_XML_FILE;

                if (Check_Control_XML_File(XMLFile))
                    returnText = CONTROL_XML_FILE;
            }

            return returnText;
        }

        private static bool Check_Safety_XML_File(XElement XMLFile)
        {
            bool ReturnValue = false;
            if ((XMLFile.FirstNode.ToString().Contains(Properties.Resources.BL_FileOperations_Triconex_attribute)) || (XMLFile.FirstNode.ToString().Contains(Properties.Resources.BL_FileOperations_Tristation_attribute)))
            {
                ReturnValue = true;
            }

            return ReturnValue;
        }
        private static bool Check_Semins_serlization_File(XElement XMLFile)
        {
            bool ReturnValue = false;
            if ((XMLFile.FirstNode.ToString().Contains(Properties.Resources.BL_FileOperations_Triconex_attribute)) || (XMLFile.FirstNode.ToString().Contains(Properties.Resources.BL_FileOperations_Tristation_attribute)))
            {
                ReturnValue = true;
            }

            return ReturnValue;
        }
        private static bool Check_Schneider_XML_File(XElement XMLFile)
        {
            bool ReturnValue = false;

            if (XMLFile.FirstNode.ToString().Contains(Properties.Resources.BL_FileOperations_Schneider_attribute) && !XMLFile.FirstNode.ToString().Contains(Properties.Resources.BL_FileOperations_Triconex_attribute))
            {
                ReturnValue = true;
            }

            return ReturnValue;
        }

        private static bool Check_Control_XML_File(XElement XMLFile)
        {
            bool ReturnValue = false;

            if (XMLFile.FirstNode.ToString().Contains(Properties.Resources.BL_FileOperations_ControlSchema))
            {
                ReturnValue = true;
            }

            return ReturnValue;
        }

        public static string FileTypeExt(string FileName)
        {
            string Return_Text = OTHER_FILE;
            string extension = string.Empty;
            if (File.Exists(FileName))
            {
                extension = Path.GetExtension(FileName);

                extension = extension.Replace(".", string.Empty);
                switch (extension.ToUpper())
                {
                    case "XML":
                        Return_Text = XML_FILE;
                        break;

                    case "XLSX":
                        Return_Text = EXCEL_FILE;
                        break;

                    case "PT2":
                        Return_Text = PT2_FILE;
                        break;

                    case "LPT":
                        Return_Text = LADDER;
                        break;

                    case "DPT":
                        Return_Text = LADDER + "DPT";
                        break;
                }
            }
            if (Directory.Exists(FileName))
            {
                Return_Text = FOLDER;
            }

            return Return_Text;
        }



        public static XElement LoadXMLFile(string XMLFileName)
        {
            XElement root = null;

            if (FileWorks.FileTypeExt(XMLFileName) == FileWorks.XML_FILE)
            {
                using (new WaitCursor())
                {
                    root = XElement.Load(XMLFileName);
                }
            }
            return root;
        }

        public static void Drop_Exeuted(string filename)
        {
            string DropFileType = string.Empty;

            DropFileType = FileWorks.FileType(filename);
            switch (DropFileType)
            {
                case FileWorks.FOLDER:
                case FileWorks.SAFETY_XML_FILE:
                case FileWorks.SCHNEIDER_XML_FILE:
                case FileWorks.LADDER:
                case FileWorks.LADDER + "DPT":
                case FileWorks.SiemensParser:
                    DropCustomXmlFile(filename, DropFileType);
                    break;
            }
        }

        private static void DropCustomXmlFile(string filename, string DropFileType)
        {
            OnSafetyTemplateDragDrop(filename, DropFileType);
            if (FileWorks.LADDER == DropFileType)
            {
                XMLOperations_RuleSet.Savefilepath(filename);
            }

            XMLOperations_RuleSet.SafetyTemplateName = filename;
        }


   
        public static void SaveExcelPackageinPath(ExcelPackage package, string path)
        {
            if (package != null)
            {
                Stream stream = File.Create(path);
                package.SaveAs(stream);
                stream.Close();
                package.Dispose();
            }
        }







        protected static void OnSafetyTemplateDragDrop(string FileName, string FileType)
        {
            DragDropEventHandler handler = SafetyTemplate_DragDrop;
            if (handler != null)
            {
                handler(FileName, FileType);
            }
        }
    }
}