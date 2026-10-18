

using OfficeOpenXml;

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using System;
using System.Text;
using System.Linq;

namespace BL
{   /// <summary>
    ///collection of common functions related to file/folder operations
    /// </summary>
    public partial class FileWorks
    {
        #region BL Functions
   


        /// <summary>
        /// check if the passed string is a valid file name
        /// </summary>
        /// <param name="inputFilePath">file name</param>
        
        private static bool IsValidFile(string inputFilePath) => System.IO.File.Exists(inputFilePath);



  

    



        /// <summary>
        /// open the explorer window and focus on a file
        /// </summary>
        /// <param name="fileName">file Name</param>
   
        private static void GetFocusForFile(string fileName)
        {
            // WinForms SendKeys is Windows-only and only ever used to focus the
            // Explorer window after a save; the headless host has nothing to focus.
        }

        /// <summary>
        /// get the file extension
        /// </summary>
        /// <param name="fileName">file name</param>
      
        public static string FileExtension(string fileName) => (File.Exists(fileName))
                                                            ? Path.GetExtension(fileName).Replace(".", string.Empty)
                                                            : string.Empty;

   
        /// <summary>
        /// open excel package
        /// </summary>
        /// <param name="filename">file name</param>
  
        public static ExcelPackage OpenExcelPackage(string filename)
        {
            var newFile = new FileInfo(filename);
            var pck = new ExcelPackage(newFile);

            return pck;
        }

        /// <summary>
        /// Save excel Package in a path
        /// </summary>
        /// <param name="workBook">Excel package</param>
        /// <param name="path">Path for saving the file</param>
    
        public static void SaveExcelPackageInPath(ExcelPackage workBook, string path)
        {
            if (workBook != null)
            {
                var stream = File.Create(path);
                workBook.SaveAs(stream);
                stream.Close();
                workBook.Dispose();
            }
        }

        /// <summary>
        /// Create folder if not exist
        /// </summary>
        /// <param name="fullPath">folder path</param>
      
        public static void CreateFolder(string fullPath)
        {
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        /// <summary>
        /// Save list of string in a text file
        /// </summary>
        /// <param name="textList">list of string</param>
        /// <param name="fileName">file name for saving</param>
     
        public static void SaveTextFile(List<string> textList, string fileName)
        {
            System.IO.File.WriteAllLines(fileName, textList.ToArray());
        }

        /// <summary>
        /// Open Excel sheet
        /// </summary>
        /// <param name="workBookDoc">Excel package</param>
        /// <param name="sheetName">Sheet Name</param>


        public static ExcelWorksheet OpenExcelSheet(ExcelPackage workBookDoc, string sheetName)
        {
            var workBook = workBookDoc.Workbook;
            ExcelWorksheet sheet = null;
            if (DoesSheetExist(sheetName, workBook))
            {
                sheet = workBook.Worksheets[sheetName];
            }

            return sheet;
        }

        /// <summary>
        /// Check Excel sheet existence
        /// </summary>
        /// <param name="sheetName">Sheet Name</param>
        /// <param name="workBook">WorkBook Name</param>
        
        public static bool DoesSheetExist(string sheetName, ExcelWorkbook workBook)
        {
            return (workBook != null)
                && workBook.Worksheets.Count > 0
                && workBook.Worksheets[sheetName] != null
                && workBook.Worksheets[sheetName].Dimension != null;
        }

        public void SaveXmlInUTF8(XElement xml, string filePath)
        {
            var stream = new MemoryStream();
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                xml.Save(writer);
                File.WriteAllBytes(filePath, stream.ToArray());
                stream.Close();
            }
        }

        /// <summary>
        /// Get the data directory for the application
        /// </summary>
        public static string GetDataDirectory()
        {
            return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
        }


        /// <summary>
        /// Get the Files contained in Folder path
        /// </summary>
        public string[] GetDirectoryFiles(string path)
        {
            return Directory.GetFiles(path);
        }

        /// <summary>
        /// Get the Files contained in Folder path with specific extension and search option (top or include all sub directories)
        /// </summary>
        public string[] GetDirectoryFilesWithExtension(string path, string extension, SearchOption selectedSearchOption)
        {
            return Directory.EnumerateFiles(path, "*.*", selectedSearchOption).Where(s => s.ToUpper().EndsWith(extension.ToUpper())).ToArray();

        }

        /// <summary>
        /// Get the Files contained in Folder path with specific extension
        /// </summary>
        public string[] GetDirectoryFilesWithExtension(string path, string extension)
        {
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(s => s.ToUpper().EndsWith(extension.ToUpper())).ToArray();
        }

        /// <summary>
        /// Get the Files contained in Folder path without their extension
        /// </summary>
        public string GetFileNameWithOutExtension(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath);
        }

        /// <summary>
        /// Get the file path
        /// </summary>
        public string GetFilePathOnly(string filePath)
        {
            return Path.GetDirectoryName(filePath);
        }

        /// <summary>
        /// Checks if a file already exist, if yes rename the uploadedFile.
        /// </summary>
        /// <param name="uploadedFile">The file to be checked</param>
        /// <returns>The name of the file after renaming if needed, if not then the same name will be renamed</returns>
        public static string CheckIfFileNeedsRenaming(string uploadedFile)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(uploadedFile);
            int suffix = 1;
            string modifiedName = fileName;
            while (System.IO.File.Exists(uploadedFile.Replace(fileName, modifiedName)))
            {
                modifiedName = fileName + "_" + suffix;
                suffix++;
            }
            return uploadedFile.Replace(fileName, modifiedName);
        }
        #endregion

    }
}