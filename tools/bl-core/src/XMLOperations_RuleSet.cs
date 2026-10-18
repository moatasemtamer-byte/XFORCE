/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 *
 * Purpose:             XML deserialization and (later also) serialization of StrategyTemplates.
 *
 *
 * Rev     DATE    Author   Description
 * --------    ------   ---------------------------------------------------------
 *
 ********************************************************************************************/

using BL.SymbolData;
using System;
using System.Collections.Generic;
using System.Linq;

//using System.Windows;
using System.Xml.Linq;

namespace BL
{
    public class XMLOperations_RuleSet
    {
        public const string PROJECT_CONFIGURATION = "PROJECT_CONFIGURATION";
        public const string INSTRUMENT_INDEX = "INSTRUMENT_INDEX";
        public const string SIGNATURE_INDEX = "SIGNATURE_INDEX";
        public const string PATH = "PATH";
        public const string CUSTOM_TEMPLATE = "CUSTOM_TEMPLATE";
        public const string Replication_ORGINALXML = "Replication_ORGINALXML";
        public const string SafetyReplication_ORGINALXML = "SafetyReplication_ORGINALXML";
        public const string SafetyReplication_SafetyAPIName = "SafetyReplication_SAFETYAPINAME";
        private static string _PathData = GetDataDirectory();
        private static string _PathApplication = GetApplicationDirectory();

        public static string GalaxyPath = System.IO.Path.Combine(_PathData, "Galaxy");

        public static string fullPath = System.IO.Path.Combine(_PathApplication, "InstanceRuleset");

        //public static string BlankfullPath = string.Format("{0}\\Ruleset\\BlankRuleset.xml", _PathData);
        public static string RulefullPath = System.IO.Path.Combine(_PathApplication, "InstanceRuleset", "Ruleset.xml");

        public static string MacroInstancefullPath = System.IO.Path.Combine(_PathApplication, "InstanceRuleset", "MacroInstance.xml");

        public static string MacroLoopPath = string.Format("{0}" + Properties.Resources.BL_MacroLoop_ExcelPath, _PathApplication);

        private static RuleSets _myRule = GettheRuleBook();//position is important here
        private static RuleSets _myBlankRule = SetBlankRule();//position is important here
        private static RuleSets _Instance;

        private static string _SafetyTemplateName = SearchForRule(PROJECT_CONFIGURATION, CUSTOM_TEMPLATE, PATH);

        private static string _SafetyReplicationAPIName = SearchForRule(PROJECT_CONFIGURATION, SafetyReplication_SafetyAPIName, PATH);

        public static string SafetyGuid = SearchForRule("GUIDStartUP", "GUID", "GUIDValue");
        public static string SafetyCounterStr = SearchForRule("GUIDStartUP", "Counter", "CounterValue");
        public static int SafetyCounter = GetSafetyCounter();

        private static int GetSafetyCounter()
        {
            bool dd = int.TryParse(SafetyCounterStr, out SafetyCounter);
            return SafetyCounter;
        }

        public static string SafetyTemplateName
        {
            get
            {
                if (_SafetyTemplateName != "")
                    return _SafetyTemplateName;
                else
                    if (System.IO.Directory.Exists(GalaxyPath))
                {
                    return GalaxyPath;
                }
                else
                {
                    return string.Empty;
                }
            }
            set
            {
                _SafetyTemplateName = value;
            }
        }

        public static string SafetyReplicationAPIName
        {
            get
            {
                return _SafetyReplicationAPIName
;
            }
            set
            {
                _SafetyReplicationAPIName = value;
            }
        }

        public static RuleSets myRule
        {
            get
            {
                return _myRule;
            }
            set
            {
                _myRule = value;
            }
        }

        public static RuleSets Instance
        {
            get
            {
                return _Instance;
            }
            set
            {
                _Instance = value;
            }
        }

        public static RuleSets myBlankRule
        {
            get
            {
                return _myBlankRule;
            }
            set
            {
                _myBlankRule = value;
            }
        }

        public static Template DeserializeTemplate(XElement loadedXml)
        {
            Template newTemplate = new Template();

            var BlockList = (from b in loadedXml.Descendants()
                             where b.Name.LocalName == Properties.Resources.BL_XMLAttribute_Block
                             select b).ToList();

            if (BlockList != null)
            {
                foreach (var block in BlockList)
                {
                    Block newSymbolBlok = new Block();

                    var CustomPropertyList = (from b in block.Descendants()
                                              where b.Name.LocalName == Properties.Resources.BL_XMLAttribute_CustomProperty
                                              select b).ToList();

                    foreach (var CustomProperty in CustomPropertyList)
                    {
                        if (CustomProperty != null)
                        {
                            Property newSymbolProperty = new Property();
                            newSymbolProperty.MyXMLProperty = CustomProperty;
                            newSymbolProperty.Name = CustomProperty.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value;

                            var Expressionist = (from HH in CustomProperty.Descendants()
                                                 where HH.Name.LocalName == Properties.Resources.BL_XMLAttribute_Expression
                                                 select HH).FirstOrDefault();

                            if (Expressionist != null)
                            {
                                newSymbolProperty.Expression = Expressionist.Value;
                            }

                            newSymbolBlok.CustomPropertyMap.Add(newSymbolProperty.Name, newSymbolProperty);
                        }
                    }
                    newSymbolBlok.Name = block.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value;

                    newSymbolBlok.MyXML = block;

                    newTemplate.SymbolBlockMap.Add(newSymbolBlok);
                }
            }

            newTemplate.Name = loadedXml.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value;
            newTemplate.MyXML = loadedXml;

            return newTemplate;
        }

        public static RuleSets DeserializeRuleSet(XElement root)
        {
            RuleSets newRuleSets = new RuleSets();

            if ((root != null))
            {
                var TemplateList = (from b in root.Descendants()
                                    where b.Name.LocalName == Properties.Resources.BL_XMLAttribute_Template
                                    select b).ToList();
                if (TemplateList != null)
                {
                    foreach (var template in TemplateList)
                    {
                        Template newtemplate = DeserializeTemplate(template);
                        newRuleSets.TemplateMap.Add(newtemplate);
                    }
                }
            }

            newRuleSets.MyXML = root;
            return newRuleSets;
        }

       

        public static string SearchForRule(string Template, string BlockName, string PropertyName)
        {
            string returnValue = "";
            if (myRule != null)
            {
                var Temp = myRule.TemplateMap.FirstOrDefault(temp => temp.Name == Template);
                if (Temp != null)
                {
                    var block = Temp.SymbolBlockMap.FirstOrDefault(blk => blk.Name.ToUpper() == BlockName.ToUpper());
                    if (block != null)
                    {
                        var prop = block.CustomPropertyMap.FirstOrDefault(prp => prp.Value.Name == PropertyName).Value;
                        if (prop != null)
                        {
                            returnValue = prop.Expression;
                        }
                    }
                }
            }

            return returnValue;
        }

        public static string SaveRule(string Template, string BlockName, string PropertyName, string PropertyValue)
        {
            string returnValue = string.Empty;
            if ((myRule != null) && (myBlankRule != null))
            {
                var Temp = XMLOperations_RuleSet.myRule.TemplateMap.FirstOrDefault(temp => temp.Name == Template);
                if (Temp == null)
                {
                    var BlankTemp = new Template(XMLOperations_RuleSet.myBlankRule.TemplateMap.FirstOrDefault());
                    if (BlankTemp != null)
                    {
                        BlankTemp.TemplateName = Template;
                        BlankTemp.Name = Template;
                        BlankTemp.MyXML.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = Template;
                        var BlankBlok = BlankTemp.SymbolBlockMap.FirstOrDefault();
                        if (BlankBlok != null)
                        {
                            BlankBlok.Name = BlockName;
                            BlankBlok.MyXML.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = BlockName;
                        }

                        var BlankProp = BlankBlok.CustomPropertyMap.FirstOrDefault().Value;
                        if (BlankProp != null)
                        {
                            BlankProp.Name = PropertyName;
                            BlankProp.MyXMLProperty.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = PropertyName;
                            BlankProp.Expression = PropertyValue;
                            BlankProp.MyXMLProperty.Descendants().FirstOrDefault().Value = PropertyValue;
                        }
                    }
                    XMLOperations_RuleSet.myRule.TemplateMap.Add(BlankTemp);
                    XMLOperations_RuleSet.myRule.MyXML.Add(BlankTemp.MyXML);
                }
                if (Temp != null)
                {
                    var block = Temp.SymbolBlockMap.FirstOrDefault(blk => blk.Name == BlockName);
                    if (block == null)
                    {
                        var BlankBlok = new Block(XMLOperations_RuleSet.myBlankRule.TemplateMap.FirstOrDefault().SymbolBlockMap.FirstOrDefault());
                        if (BlankBlok != null)
                        {
                            BlankBlok.Name = BlockName;
                            BlankBlok.MyXML.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = BlockName;
                        }
                        var BlankProp = BlankBlok.CustomPropertyMap.FirstOrDefault().Value;
                        if (BlankProp != null)
                        {
                            BlankProp.Name = PropertyName;
                            BlankProp.MyXMLProperty.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = PropertyName;
                            BlankProp.Expression = PropertyValue;
                            BlankProp.MyXMLProperty.Descendants().FirstOrDefault().Value = PropertyValue;
                        }
                        Temp.SymbolBlockMap.Add(BlankBlok);
                        Temp.MyXML.Descendants().FirstOrDefault().Add(BlankBlok.MyXML);
                    }

                    if (block != null)
                    {
                        var prop = block.CustomPropertyMap.FirstOrDefault(prp => prp.Value.Name == PropertyName).Value;
                        if (prop == null)
                        {
                            var BlankProp = new Property(XMLOperations_RuleSet.myBlankRule.TemplateMap.FirstOrDefault().SymbolBlockMap.FirstOrDefault().CustomPropertyMap.FirstOrDefault().Value);
                            if (BlankProp != null)
                            {
                                BlankProp.Name = PropertyName;
                                BlankProp.MyXMLProperty.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = PropertyName;
                                BlankProp.Expression = PropertyValue;
                                BlankProp.MyXMLProperty.Descendants().FirstOrDefault().Value = PropertyValue;
                            }
                            block.CustomPropertyMap.Add(PropertyName, BlankProp);
                            block.MyXML.Descendants().FirstOrDefault().Add(BlankProp.MyXMLProperty);
                        }
                        if (prop != null)
                        {
                            prop.Expression = PropertyValue;
                            prop.MyXMLProperty.Descendants().FirstOrDefault().Value = PropertyValue;
                        }
                    }
                }
            }
            else
            {
                string Status = "Save is disabled due to configuration file is missed ";
                BL.Logger.LogError(null, Status);
            }

            return returnValue;
        }

        public static RuleSets GettheRuleBook()
        {
            myRule = (!System.IO.File.Exists(RulefullPath)) ? SetBlankRule() : SetmyRule(RulefullPath);
            return myRule;
            //if (!System.IO.Directory.Exists(fullPath))
            //{
            //    System.IO.Directory.CreateDirectory(fullPath);

            //    myRule = SetmyRule(BlankfullPath);
            //    // _Instance = SetmyRule(BlankfullPath);
            //}
            //else
            //{
            //    if (System.IO.File.Exists(RulefullPath))
            //    {
            //        myRule = SetmyRule(RulefullPath);
            //    }
            //    else
            //    {
            //        myRule = SetmyRule(BlankfullPath);
            //    }
            //}
            //_Instance = SetmyRule(BlankfullPath);
            //return myRule;
        }

        public static RuleSets SetmyRule(string Path)
        {
            XElement root = null;
            RuleSets returnRuleset = null;

            try
            {
                if (System.IO.File.Exists(Path))
                    root = XElement.Load(Path);
            }
            catch (Exception exx)
            {
            }

            if (root == null)
                return returnRuleset;

            if (root != null)
            {
                returnRuleset = DeserializeRuleSet(root);
            }
            return returnRuleset;
        }

        public static RuleSets SetBlankRule()
        {
            //string temp3 = string.Format("{0}\\Ruleset\\BlankRuleset.xml", _PathData);
            //_Instance = SetmyRule(BlankfullPath);
            return DeserializeRuleSet(XElement.Parse(Properties.Resources.BlankRuleset));
        }

        public static void SaveSession()
        {
            if (myRule != null)
            {
                XMLOperations_RuleSet.myRule.MyXML.Save(RulefullPath);
            }
        }

        public static void IteriateProperies(string StrategyName, string macroName, string Blockname, Dictionary<string, string> Properties)
        {
            foreach (var key in Properties.Keys)
            {
                XMLOperations_RuleSet.AddInstanceValue(StrategyName, macroName, Blockname, key, Properties[key]);
            }
        }

        public static string AddInstanceValue(string Template, string macroName, string BlockName, string PropertyName, string PropertyValue)
        {
            string returnValue = "";
            var Temp = XMLOperations_RuleSet.Instance.TemplateMap.FirstOrDefault(temp => temp.Name == macroName);
            if (Temp == null)
            {
                var BlankTemp = new Template(XMLOperations_RuleSet.myBlankRule.TemplateMap.FirstOrDefault());
                if (BlankTemp != null)
                {
                    BlankTemp.TemplateName = Template;
                    BlankTemp.Name = macroName;
                    BlankTemp.MyXML.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = Template;
                    XAttribute InstanceName = new XAttribute(Properties.Resources.BL_XMLAttribute_Instance, macroName);
                    BlankTemp.MyXML.Add(InstanceName);
                    var BlankBlok = BlankTemp.SymbolBlockMap.FirstOrDefault();
                    if (BlankBlok != null)
                    {
                        BlankBlok.Name = BlockName;
                        BlankBlok.MyXML.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = BlockName;
                    }

                    var BlankProp = BlankBlok.CustomPropertyMap.FirstOrDefault().Value;
                    if (BlankProp != null)
                    {
                        BlankProp.Name = PropertyName;
                        BlankProp.MyXMLProperty.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = PropertyName;
                        BlankProp.Expression = PropertyValue;
                        BlankProp.MyXMLProperty.Descendants().FirstOrDefault().Value = PropertyValue;
                    }
                }
                XMLOperations_RuleSet.Instance.TemplateMap.Add(BlankTemp);
                XMLOperations_RuleSet.Instance.MyXML.Add(BlankTemp.MyXML);
            }
            if (Temp != null)
            {
                var block = Temp.SymbolBlockMap.FirstOrDefault(blk => blk.Name == BlockName);
                if (block == null)
                {
                    var BlankBlok = new Block(XMLOperations_RuleSet.myBlankRule.TemplateMap.FirstOrDefault().SymbolBlockMap.FirstOrDefault());
                    if (BlankBlok != null)
                    {
                        BlankBlok.Name = BlockName;
                        BlankBlok.MyXML.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = BlockName;
                    }
                    var BlankProp = BlankBlok.CustomPropertyMap.FirstOrDefault().Value;
                    if (BlankProp != null)
                    {
                        BlankProp.Name = PropertyName;
                        BlankProp.MyXMLProperty.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = PropertyName;
                        BlankProp.Expression = PropertyValue;
                        BlankProp.MyXMLProperty.Descendants().FirstOrDefault().Value = PropertyValue;
                    }
                    Temp.SymbolBlockMap.Add(BlankBlok);
                    Temp.MyXML.Descendants().FirstOrDefault().Add(BlankBlok.MyXML);
                }

                if (block != null)
                {
                    var prop = block.CustomPropertyMap.FirstOrDefault(prp => prp.Value.Name == PropertyName).Value;
                    if (prop == null)
                    {
                        var BlankProp = new Property(XMLOperations_RuleSet.myBlankRule.TemplateMap.FirstOrDefault().SymbolBlockMap.FirstOrDefault().CustomPropertyMap.FirstOrDefault().Value);
                        if (BlankProp != null)
                        {
                            BlankProp.Name = PropertyName;
                            BlankProp.MyXMLProperty.Attribute(Properties.Resources.BL_XMLAttribute_Name).Value = PropertyName;
                            BlankProp.Expression = PropertyValue;
                            BlankProp.MyXMLProperty.Descendants().FirstOrDefault().Value = PropertyValue;
                        }
                        block.CustomPropertyMap.Add(PropertyName, BlankProp);
                        block.MyXML.Descendants().FirstOrDefault().Add(BlankProp.MyXMLProperty);
                    }
                    if (prop != null)
                    {
                        prop.Expression = PropertyValue;
                        prop.MyXMLProperty.Descendants().FirstOrDefault().Value = PropertyValue;
                    }
                }
            }

            return returnValue;
        }

        public static string GetDataDirectory()
        {
            // ClickOnce data directory is a Windows-only concept; the headless host is
            // never network-deployed, so always resolve next to the entry assembly.
            return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
        }

        private static string GetApplicationDirectory()
        {
            return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
        }

        public static void Savefilepath(string path)
        {
            if (!System.IO.Directory.Exists(fullPath))
            {
                System.IO.Directory.CreateDirectory(fullPath);
            }
            XMLOperations_RuleSet.SaveRule(PROJECT_CONFIGURATION, path, path, path);
            SaveSession();
        }

        public static void SaveGuid(string guid)
        {
            if (!System.IO.Directory.Exists(fullPath))
            {
                System.IO.Directory.CreateDirectory(fullPath);
            }
            XMLOperations_RuleSet.SaveRule("GUIDStartUP", "GUID", "GUIDValue", guid);
            SaveSession();
        }

        public static void SaveCounter(int dd)
        {
            if (!System.IO.Directory.Exists(fullPath))
            {
                System.IO.Directory.CreateDirectory(fullPath);
            }
            XMLOperations_RuleSet.SaveRule("GUIDStartUP", "Counter", "CounterValue", dd.ToString());
            SaveSession();
        }

        public static void savewatchlog(List<string> mylist)
        {
            if (!System.IO.Directory.Exists(fullPath))
            {
                System.IO.Directory.CreateDirectory(fullPath);
            }
            System.IO.File.WriteAllLines(System.IO.Path.Combine(fullPath, "log.txt"), mylist.ToArray());
        }
    }
}