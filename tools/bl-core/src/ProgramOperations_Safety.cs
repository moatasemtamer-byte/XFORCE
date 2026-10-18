using Microsoft.VisualBasic;
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
using System.Linq;
using System.Xml.Linq;

namespace BL
{
    public class ProgramOperations_Safety
    {
        //serlize outputvariable
        //template b
        public static string TemplateSelction ;
        public static int InPOutStageWidth = 20;
        //public static int PageWidthD = 262;//for template d
        public static int PageWidthD;
        // public static int PageCenterD = 100;//for templat D
        public static int PageCenterD;
        public static int SpaceXDifForBlocks;
        public static int SpaceXDifForlineConnection;
        public static int CoilContactWidth;
        public static int CoilContactHeight;
        public static int BlockWidth = 12;
        public static int BlocksYdifferenceTunning = 2;
        public static int Stagediff;
        public static int StageYdiff = 0;
        public static int Networkdiff = 0;
        public static string Comment_X_begin;
        public static int PageEnd;
        private static List<string> blocknames = new List<string>();


        public static void UpdateSetting()
        {
            InPOutStageWidth = 20;
            InPOutStageWidth= TemplateSelction == "B" ? 20 : 22;
           // PageWidthD = TemplateSelction == "B" ? 174 : 262;//for templat b NESMA
            PageWidthD = TemplateSelction == "B" ? 174 : 278;  //NESMA                                              // public static int PageCenterD = 100;//for templat D
            PageCenterD = TemplateSelction == "B" ? 79 : 100;//for templat b
            SpaceXDifForBlocks = TemplateSelction == "B" ? 15 : 20;//for templat b
            SpaceXDifForlineConnection = TemplateSelction == "B" ? 12 : 15;//for templat b
            //CoilContactWidth = TemplateSelction == "B" ? 16 : 18;nESMA
           // CoilContactHeight= TemplateSelction == "B"? 10 : 8;//NESMA
            CoilContactWidth = TemplateSelction == "B" ? 16 : 20; //NESMA
            //CoilContactHeight = TemplateSelction == "B" ? 10 : 20;//NESMA
            CoilContactHeight = TemplateSelction == "B" ? 10 : 11;//NESMA
            BlocksYdifferenceTunning = TemplateSelction == "B" ? 2 : 0;//NESMA
            BlockWidth = 12;
            Stagediff = TemplateSelction == "B" ? 10 : 5;
            PageEnd= TemplateSelction == "B" ? 117 : 202;
            StageYdiff = 0;
            Networkdiff = 0;
            Comment_X_begin = TemplateSelction == "B" ? "57" : "113";
        }
        public static outputVariables DeserializeSafetyoutputVariables(XElement thisoupvar, string fatherbBLkID)
        {
            outputVariables newouppin = new outputVariables();

            var varO = from O in thisoupvar.Descendants()
                       where O.Name.LocalName == "variable"
                       select O;

            foreach (XElement thispvarO in varO)
            {
                //variable newvarO = new variable();

                //here change

                variable newOvariable = new variable();
                newOvariable.fatherblockID = fatherbBLkID;
                newOvariable.MyXML = thispvarO;
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
                newvar.MyXML = thispvar;
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

        internal static void BuildPACKLogic(ref SafetyTemplate currentst, ref SortedDictionary<int, int> ds)
        {
            string logic = currentst.Template;
            String rEG = logic.Substring(0, logic.IndexOf("="));
            int i = 1;
            adjustcomma(ref logic);
            List<string> splitedlogic = logic.Split('@').ToList();
            splitedlogic.RemoveAt(0);
            inOutVariable Rvar = currentst.inOutVariableMap.FirstOrDefault(x => x.Value.expressionMap.Tagname == "REG").Value;
            if (Rvar != null)
            {
                XMLOperations_safety.ModifySafetyTemplateTag(ref currentst, "REG", rEG);
            }
            foreach (var tag in splitedlogic)
            {
                string newtag = tag.Replace(")", "").Replace(" ", "");
                string oldtag = "TAG" + i.ToString();
                inOutVariable var = currentst.inOutVariableMap.FirstOrDefault(x => x.Value.expressionMap.Tagname == oldtag).Value;
                if (var != null)
                {
                    XMLOperations_safety.ModifySafetyTemplateTag(ref currentst, oldtag, newtag);
                }
                i++;
            }
        }

        internal static void BuildLogic(ref SafetyTemplate st, ref SortedDictionary<int, int> IDs, List<string> mismatches)
        {
            AddInputStage(ref st);
            AddOutputStage(ref st);
            ChangeCoilContactWidthForSTemplate(ref st); 
            foreach (coil onecoil in st.SafetycoilMap.Values)
            {
                coil currentcoil = onecoil;
                // SafetyBlock blankblock = XMLOperations_LadderLogic.ReadBlankBlock();
                checkuncompletelogic(onecoil, mismatches);
                BL.Logger.LogFile(null, "Network #"+ onecoil.Networknumber +" :Begin to import "+ onecoil.Name + " into PT2");
                BuildcoilLogic(ref currentcoil, ref st, ref IDs,mismatches);
                BL.Logger.LogFile(null, "Network #" + onecoil.Networknumber + " :import " + onecoil.Name + " into PT2 Succeeded");
            }
            SearchAboutNotDoneLogicINSTemplate(st, mismatches);
        
        }

        private static void SearchAboutNotDoneLogicINSTemplate(SafetyTemplate st, List<string> mismatches)
        {

            var notdonecon = (from S in st.SafetycontactMap.Values
                              where S.Calculated == "Not Done"
                              select S).ToList();
          
            foreach(contact mycon in notdonecon)
            {
                // string mis = "Network # " + mycon.Networknumber + ": can't be converted:this network need manual work,contact " + mycon.Name + " is not connected";
                string mis = "Network # " + mycon.Networknumber + ": can't be converted:this network need manual work";
                if (!mismatches.Contains(mis))
                {
                    mismatches.Add(mis);
                }
            }
        }

        private static void checkuncompletelogic(coil onecoil, List<string> mismatches)
        {
            string[] completedcoillogic = new string[] { "TDD","" , "TDE","PLS","NOT","LCH" ,"TMR","CTU"};
            if (!completedcoillogic.Contains(onecoil.Coiltype) &&!(onecoil.Coiltype == "RST" && onecoil.twinCoiltype == "LCH"))
            {
              
                mismatches.Add("Network # " + onecoil.Networknumber + ": logic need to be added for coil "+ onecoil.Name+" to represent ("+ onecoil.Coiltype+")");
            }

            string[] completedcontactogic = new string[] { "/", ">",">=", "<", "<=",""," ", "^","v" ,"==","<>"};
            foreach (var contact in onecoil.MyLogic.LogiccontactMap.Values)
            {
                if(!completedcontactogic.Contains(contact.typeName))
                {
                    string error ="Network # " + onecoil.Networknumber + ": logic need to be added for contact " + contact.Name.Replace("SYS", "$") + " to represent ]" + contact.typeName + "[";

                    if (!mismatches.Contains(error))
                    {
                        mismatches.Add(error);
                    }
                }

                if (contact.Name.Contains("SYS"))
                {
                    string error = "Network # " + onecoil.Networknumber + ": logic need to be added for contact " + contact.Name.Replace("SYS", "$") + " to represent system logic";
                    if(!mismatches.Contains(error))
                    {
                        mismatches.Add(error);
                    }
                    
                }
            }
        }
        

        private static void BuildcoilLogic(ref coil currentcoil, ref SafetyTemplate st, ref SortedDictionary<int, int> IDs,List<string> mismatches)
        {
            int stage = 1;
           
                if (currentcoil.Logic.Contains("OR") || currentcoil.Logic.Contains("AND(") || currentcoil.Logic.Contains("="))
                {
                    SerializeLogic(ref currentcoil, ref stage, ref st, ref IDs, mismatches);
                    SearchAboutNotDoneLogic(currentcoil, mismatches);
                    SearchAboutCoilNotInSheetRange(currentcoil, mismatches);
                }
         
        }

       
        // TEMPLATE B MODIFICATION MAXIMUM Y
        private static void SearchAboutCoilNotInSheetRange(coil currentcoil, List<string> mismatches)
        {
            //  throw new NotImplementedException();
            var notdonecon = (from S in currentcoil.MyLogic.LogiccontactMap.Values
                              where int.Parse(S.positionMap.Y) >= PageEnd
                              select S).FirstOrDefault();
            if (notdonecon != null)
            {
                // string mis= "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work,contact "+ notdonecon.Name +" is not connected";
                string mis = "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work";
                if (!mismatches.Contains(mis))
                {
                    mismatches.Add(mis);
                }
            }
          }

        private static void SearchAboutNotDoneLogic(coil currentcoil, List<string> mismatches)
        {
           var notdonecon= (from S in currentcoil.MyLogic.LogiccontactMap.Values
                            where  S.Calculated == "Not Done"
                            select S).FirstOrDefault();
            if(notdonecon!=null)
            {
                // string mis= "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work,contact "+ notdonecon.Name +" is not connected";
                string mis = "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work";
                if (!mismatches.Contains(mis))
                {
                    mismatches.Add(mis);
                }

                foreach(contact mycon in currentcoil.MyLogic.LogiccontactMap.Values)
                {
                    mycon.Calculated = "Not Done and Logged";
                }
                
            }
            var con = currentcoil.connectionPointInMap.connectionMap.FirstOrDefault();
            if(con==null&& notdonecon==null)
            {
                // string mis = "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work,coil " + currentcoil.Name + " is not connected";
                string mis = "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work";
                if (!mismatches.Contains(mis))
                {
                    mismatches.Add(mis);
                }
            }
        }

        private static void SerializeLogic(ref coil currentcoil, ref int stage, ref SafetyTemplate st, ref SortedDictionary<int, int> IDs, List<string> mismatches)
        {
            string oldlogic = currentcoil.Logic;
            string logic = currentcoil.Logic.Replace("!", "");
            currentcoil.Logic = currentcoil.Logic.Replace("!", "");
            string parrellellogic = currentcoil.Parrallelcoilstring;
            string blockoutputparam = "";
            adjustcomma(ref logic);
            List<string> splitedlogic = logic.Split('@').ToList();
            List<string> splitedparrallellogic = parrellellogic.Split('/').Where(x => x != "").ToList();
            //if(splitedlogic[0].Contains("TCJ"))
            //{

            //}
            // if (splitedlogic[0].Contains("And") || splitedlogic[0].Contains("OR"))
            if (splitedparrallellogic.Count() <= 1)
            {
                if (splitedlogic[0].ToUpper().Contains("AND(") || splitedlogic[0].Contains("OR") || splitedlogic[0].Contains("FB04_DI")
                    || splitedlogic[0].Contains("TRIG")|| splitedlogic[0] == "TCJ_CONV("|| splitedlogic[0] == "SR("|| splitedlogic[0] == "RS(" || splitedlogic[0] == "TMR_I("|| splitedlogic[0] == "CTU("
                     || splitedlogic[0].Contains("GT")|| splitedlogic[0].Contains("TDD_I")|| splitedlogic[0].Contains("TDE_I")|| splitedlogic[0].Contains("TP_I")|| splitedlogic[0].Contains("TP(")
                      || (splitedlogic[0].Contains("TO")) || splitedlogic[0] == "AIN(" || splitedlogic[0] == "SCALE(" || splitedlogic[0].Contains("STFB")|| splitedlogic[0] == "MEDSEL("|| splitedlogic[0] == "PERDEV("|| splitedlogic[0] =="EQ("|| splitedlogic[0] == "NE("
                     || splitedlogic[0].Replace("(", "") == "GE" || splitedlogic[0].Contains("LT") || splitedlogic[0].Contains("LE") || (splitedlogic.Count() == 1 && splitedlogic[0] != ""))
                
                {
                    string typeName = splitedlogic[0].Replace("(", "").ToUpper();
                    if (splitedlogic.Count() == 1 && splitedlogic[0] != "")
                    {
                        if (oldlogic.Contains("!")|| currentcoil.Coiltype=="NOT")
                        {
                            typeName = "NOT";
                            blockoutputparam = "NOT";
                        }
                        else
                        {
                            typeName = "MOVE";
                            blockoutputparam = "OUT1";
                        }
                    }
                    else
                    {
                        blockoutputparam = typeName;
                        splitedlogic.RemoveAt(0);
                        splitedlogic[splitedlogic.Count() - 1] = splitedlogic[splitedlogic.Count() - 1].Replace("))", ")");
                    }

                    SafetyBlock newBlock = XMLOperations_LadderLogic.newBlockType(typeName, st.SheetNumber);

                    if (!string.IsNullOrEmpty(newBlock.instanceName))
                    {
                        XMLOperations_safety.ModifyBlockName(typeName + "_" + currentcoil.Name, newBlock);
                    }
                    string newID = (IDs.ElementAt(IDs.Count() - 1).Value + 1).ToString();
                    IDs.Add(int.Parse(newID), int.Parse(newID));

                    newBlock.Logic = splitedlogic;

                    XMLOperations_LadderLogic.ChangeBlockID(ref newBlock, newID);
                    blockoutputparam = Getoutparameter(blockoutputparam);
                    ChangeCoilConnectionRef(ref currentcoil, newBlock.localId, blockoutputparam);
                    CheckoutputInverted(currentcoil, newBlock, blockoutputparam);
                    newBlock.Stage = stage;
                    currentcoil.MyLogic.LogicBlockMap.Add(newID, newBlock);
                  
                    ReadblockinputLogic(ref newBlock, splitedlogic, ref currentcoil, ref stage, ref IDs, mismatches);
                  
                    ADDStagesToTemplate(ref st, currentcoil);
                    
                    DrawLogicCoil(ref currentcoil, ref st, ref IDs,mismatches);

                    DrawNetworkseprator(ref IDs, ref st);
                }
                else
                {
                    mismatches.Add("Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work");
                }
            }
            else
            {

                Drawparrallelcoil(ref currentcoil, ref st);
                DrawNetworkseprator(ref IDs, ref st);
            }
        }

        private static void ChangeCoilContactWidthForSTemplate(ref SafetyTemplate st)
        {
            foreach(var currentcoil in  st.SafetycoilMap.Values)
            {
                ChangeCoilContactWidth(currentcoil);
            }

            foreach (var currentcontact in st.SafetycontactMap.Values)
            {
                ChangeContactWidth(currentcontact);
            }

        }

        private static void ChangeCoilContactWidth(coil currentcoil)
        {
            foreach (var contact in currentcoil.MyLogic.LogiccontactMap.Values)
            {
                ChangeContactWidth(contact);
            }

            XElement ioVariable = currentcoil.MyXML;
            ioVariable.Attribute("width").Value = CoilContactWidth.ToString();
            ioVariable.Attribute("height").Value = CoilContactHeight.ToString();
            currentcoil.width = CoilContactWidth.ToString();
            currentcoil.height = CoilContactHeight.ToString();
        }

        private static void ChangeContactWidth(contact contact)
        {
            XElement conVariable = contact.MyXML;
            conVariable.Attribute("width").Value = CoilContactWidth.ToString();
            conVariable.Attribute("height").Value = CoilContactHeight.ToString();
            contact.width = CoilContactWidth.ToString();
            contact.height = CoilContactHeight.ToString();
        }

        private static void CheckoutputInverted(coil currentcoil, SafetyBlock newBlock, string blockoutputparam)
        {
            if(currentcoil.Coiltype.ToUpper()=="NOT" && newBlock.typeName!="MOVE")
            {
                var outList = (from e in newBlock.MyXML.Descendants()
                               where e.Name.LocalName == "outputVariables"
                               select e).FirstOrDefault();

                if (outList != null)
                {
                    var varO = (from f in outList.Descendants()
                                where f.Name.LocalName == "variable" && f.Attribute("formalParameter").Value == blockoutputparam
                                select f).FirstOrDefault();
                    if (varO != null)
                    {
                        varO.Add(new XAttribute("negated", "true"));
                    }
                }

            }
        }

        private static void Drawparrallelcoil(ref coil currentcoil, ref SafetyTemplate st)
        {
            string parrellellogic = currentcoil.Parrallelcoilstring;
            string currentcoil_Logic = currentcoil.Logic;

            List<string> splitedparrallellogic = parrellellogic.Split('/').Where(x => x != "").ToList();

            if (splitedparrallellogic.Count() > 1)
            {
                string Coiltype = parrellellogic.Substring(parrellellogic.IndexOf("["), parrellellogic.Length - parrellellogic.IndexOf("["));
                string parrellelcoil = splitedparrallellogic.Last().Replace(Coiltype, "");
                Coiltype = Coiltype.Replace("[", "").Replace("]", "");
                if (Coiltype != "")
                {
                }
                coil myorginalcoil = st.SafetycoilMap.Values.LastOrDefault(x => x.Name == parrellelcoil && x.Coiltype == Coiltype &&
                 x.Logic.Substring(x.Logic.IndexOf("="), x.Logic.Length - x.Logic.IndexOf("="))
                    == currentcoil_Logic.Substring(currentcoil_Logic.IndexOf("="), currentcoil_Logic.Length - currentcoil_Logic.IndexOf("=")));
                if (myorginalcoil != null)
                {
                    int coilheight = CoilContactHeight;
                    if (myorginalcoil.height != null)
                    {
                        coilheight = int.Parse(myorginalcoil.height);
                    }
                    int countprvcoil = splitedparrallellogic.Count() - 1;
                    int Addedhieght = (coilheight + 2) * countprvcoil;
                    position myposition = ChangePostionPoint(myorginalcoil.positionMap, 0, Addedhieght);//need update
                    ChangeCoilPostion(ref currentcoil, myposition);
                    var myconn = myorginalcoil.connectionPointInMap.connectionMap.FirstOrDefault();
                    if (myconn != null)
                    {
                        ChangeCoilConnectionRef(ref currentcoil, myconn.refLocalId, myconn.formalParameter);
                    }

                    // relPosition rel = myorginalcoil.connectionPointInMap.relPositionMap.FirstOrDefault();
                    // position orginalcoilin = ChangePostionPoint(myorginalcoil.positionMap, int.Parse(rel.X), int.Parse(rel.Y));

                    relPosition relparrallel = currentcoil.connectionPointInMap.relPositionMap.FirstOrDefault();
                    if (relparrallel == null)
                    {
                        relparrallel = new relPosition("-1", "1");
                    }
                    position parrallelcoilin = ChangePostionPoint(myposition, int.Parse(relparrallel.X), int.Parse(relparrallel.Y));
                    // List<position> myList = connectbyLshape(orginalcoilin, parrallelcoilin, 10);
                    var myconnorg = myorginalcoil.connectionPointInMap.connectionMap.FirstOrDefault();
                    if (myconnorg != null)
                    {
                        position outblockpos = myconnorg.positionMap.LastOrDefault();
                        // myList.Add(outblockpos);
                        //AddpostionListToConnection(ref currentcoil, myList);
                        List<position> myList = GetconnectionPointList(parrallelcoilin, outblockpos, 15);

                        AddpostionListToConnection(ref currentcoil, myList);
                        string newEndY = (int.Parse(myposition.Y) + coilheight ).ToString();
                        ChangeInputAndOutputStagesEndY(newEndY, ref st);

                        RemovelastNetwork(ref st);
                    }
                    else
                    {
                        //may be warning here
                    }

                    // var mynetwork=  st.n.LastOrDefault().Value;
                }
            }
        }

        private static void RemovelastNetwork(ref SafetyTemplate st)
        {
            var sheet = st.MyXMLSheet;
            var inpList = (from e in sheet.Descendants()
                           where e.Name.LocalName == "NETWORKDIVIDER"
                           select e).ToList();

            if (inpList.Count > 0)
            {
                XElement thisinpvar = inpList.LastOrDefault();
                //  thisinpvar.RemoveNodes();
                thisinpvar.Remove();
            }
        }

        private static List<position> connectbyLshape(position orginalcoilin, position parrallelcoilin, int diffx)
        {
            List<position> myList = new List<position>();
            myList.Add(parrallelcoilin);

            string decpacX = (int.Parse(parrallelcoilin.X) - diffx).ToString();
            position sameY = new position(decpacX, parrallelcoilin.Y);
            myList.Add(sameY);
            position samYorginal = new position(decpacX, orginalcoilin.Y);
            myList.Add(samYorginal);

            return myList;
        }

        private static string Getoutparameter(string blockoutputparam)
        {
            if (blockoutputparam == "FB04_DI")
            {
                blockoutputparam = "STATUS_DCS";
            }

            if (blockoutputparam.Contains("TRIG") || blockoutputparam.Contains("TO")|| blockoutputparam.Contains("TP")|| blockoutputparam.Contains("TMR_I")|| blockoutputparam.Contains("CTU"))
            {
                blockoutputparam = "Q";
            }
            if (blockoutputparam.Contains("GE"))
            {
                blockoutputparam = ">=";
            }
            if (blockoutputparam.Contains("GT"))
            {
                blockoutputparam = ">";
            }
            if (blockoutputparam.Contains("EQ"))
            {
                blockoutputparam = "=";
            }
            if (blockoutputparam.Contains("NE"))
            {
                blockoutputparam = "<>";
            }

            if (blockoutputparam.Contains("LE") && !blockoutputparam.Contains("SCALE"))
            {
                blockoutputparam = "<=";
            }
            if (blockoutputparam.Contains("LT"))
            {
                blockoutputparam = "<";
            }
            if (blockoutputparam.Contains("STFB"))
            {
                blockoutputparam = "OUT1";
            }
            if (blockoutputparam.Contains("TDD_I")|| blockoutputparam.Contains("TDE_I")|| blockoutputparam.Contains("SR")|| blockoutputparam.Contains("RS"))
            {
                blockoutputparam = "Q1";
            }
            if (blockoutputparam.Contains("MEDSEL"))
            {
                blockoutputparam = "BIF";
            }
            return blockoutputparam;
        }

        private static void DrawNetworkseprator(ref SortedDictionary<int, int> ids, ref SafetyTemplate st)
        {
            string newID = (ids.ElementAt(ids.Count() - 1).Value + 1).ToString();
            ids.Add(int.Parse(newID), int.Parse(newID));
            XNamespace ts = "http://www.triconex.com/controller/03/2008";
            //string name =  ts + "NETWORKDIVIDER";
            XElement MyXML = new XElement(ts + "NETWORKDIVIDER");
            MyXML.Add(new XAttribute("Type", "HORIZONTAL"));
            MyXML.Add(new XAttribute("LocalId", newID));
            MyXML.Add(new XAttribute("X", "0"));

            //int endy = st.StageMap.OrderByDescending(x => x.Value.EndYdirection).FirstOrDefault(x => x.Value.Type == "Input").Value.EndYdirection + 4;
            //int endy = st.StageMap.OrderByDescending(x => x.Value.EndYdirection).FirstOrDefault().Value.EndYdirection+4 ;//nesma
            int endy = st.StageMap.OrderByDescending(x => x.Value.EndYdirection).FirstOrDefault().Value.EndYdirection + 2;
            MyXML.Add(new XAttribute("Y", endy.ToString()));
            var sheet = st.MyXMLSheet;
            sheet.Add(MyXML);
            //  sheet.ToString().Replace("<NETWORKDIVIDER", "<ts:NETWORKDIVIDER");
        }

        private static void adjustcomma(ref string logic)
        {
            logic = logic.Replace(",,", ",");
            if(logic.Contains("<>")|| logic.Contains("=="))
            {

            }
            if ((logic.Contains(",") || logic.Contains("(")) && !logic.Contains("@") || (logic.Contains(">")|| logic.Contains("<>") || logic.Contains("==")
                || (logic.Contains("<") || (logic.Contains("^") ||
                (logic.Contains("v"))))))
            {
                //logic= logic.Replace("AND(", "AND(@");
                //logic=logic.Replace("OR(", "OR(@");
                //logic = logic.Replace("FB04_DI(", "FB04_DI(@");
                logic = logic.Replace("v/", "F_TRIG(");
                logic = logic.Replace("^/", "R_TRIG(");
                logic = logic.Replace("<>/", "NE(");
                logic = logic.Replace(">=/", "GE(");
                logic = logic.Replace(">/", "GT(");
                logic = logic.Replace("<=/", "LE(");
                logic = logic.Replace("</", "LT(");
                logic = logic.Replace("==/", "EQ(");
                
                logic = logic.Replace("/", ")");
                logic = logic.Replace("(", "(@");

                logic = logic.Replace(",", "@");
            }
            if (logic.Contains("="))
            {
                logic = logic.Split('=').ToList()[1];
            }
        }

        private static void ChangeCoilConnectionRef(ref coil coil, string reflocalId, string typeName)
        {
            // coil.connectionPointInMap=new contactconnectionPointIn();
            relPosition myrel = new relPosition("-1", "1");
            connection myconnection = new connection(reflocalId, typeName);
            coil.connectionPointInMap.relPositionMap = new List<relPosition>();
            coil.connectionPointInMap.relPositionMap.Add(myrel);
            var myfirstconnection = coil.connectionPointInMap.connectionMap.FirstOrDefault();
            //coil.connectionPointInMap.connectionMap.FirstOrDefault().formalParameter = typeName;
            //coil.connectionPointInMap.connectionMap.FirstOrDefault().refLocalId = reflocalId;
            if (myfirstconnection != null)
            {
                myfirstconnection.formalParameter = typeName;
                myfirstconnection.refLocalId = reflocalId;
            }
            else
            {
                coil.connectionPointInMap.connectionMap.Add(myconnection);
            }

            var inpList = (from e in coil.MyXML.Descendants()
                           where e.Name.LocalName == "connectionPointIn"
                           select e).ToList();

            if (inpList.Count > 0)
            {
                XElement thisinpvar = inpList[0];
                thisinpvar.RemoveNodes();
                thisinpvar.Add(myrel.MyXML);
                thisinpvar.Add(myconnection.MyXML);
            }
        }

        private static void ADDStagesToTemplate(ref SafetyTemplate st, coil currentcoil)
        {
            foreach (var myblock in currentcoil.MyLogic.LogicBlockMap.Values)
            {
                //if(myblock.typeName=="OR"|| myblock.typeName=="AND" || myblock.typeName.Contains("TRIG")||
                //    )
                //{
                if (st.StageMap.Values.FirstOrDefault(x => x.Order == myblock.Stage) == null)
                {
                    Stage mystage = new Stage();
                    mystage.Order = myblock.Stage;
                    mystage.Type = "Logic";

                    st.StageMap.Add(mystage.Order.ToString(), mystage);
                }
                // }
            }
        }

        private static void AddInputStage(ref SafetyTemplate st)
        {
            for (int i = -1; i >= -2; i--)
            {
                Stage newStage = new Stage();
                newStage.Order = i;
                newStage.Type = "Input";
                newStage.EndYdirection = Math.Abs(i + 1);
                newStage.Starting.X = (Math.Abs(i + 1) * InPOutStageWidth).ToString();
                //  newStage.DifferenceY = 9;NESMA
                newStage.DifferenceY = CoilContactHeight + 1;
                if (st.StageMap.Keys.FirstOrDefault(x => x == i.ToString()) == null)
                    st.StageMap.Add(i.ToString(), newStage);
            }
        }

        private static void AddOutputStage(ref SafetyTemplate st)
        {
            for (int i = -1; i >= -2; i--)
            {
                Stage newStage = new Stage();
                newStage.Order = i + 100;
                newStage.Type = "Output";
                newStage.EndYdirection = i + 2;
                // newStage.Starting.X = (282 + (Math.Abs((i+1))* InPOutStageWidth)).ToString();
                //  newStage.Starting.X = (170 + (Math.Abs((i + 1)) * InPOutStageWidth)).ToString();
                newStage.Starting.X = (PageWidthD + (Math.Abs((i + 1)) * InPOutStageWidth)).ToString();
                //newStage.DifferenceY = 8;  //NESMA
                newStage.DifferenceY = CoilContactHeight ;
                if (st.StageMap.Keys.FirstOrDefault(x => x == newStage.Order.ToString()) == null)
                    st.StageMap.Add(newStage.Order.ToString(), newStage);
            }
        }
        //template b
        private static void DrawLogicCoil(ref coil currentcoil, ref SafetyTemplate St, ref SortedDictionary<int, int> ids,List<string> mismatches)
        {
            SafetyBlock curblock = currentcoil.MyLogic.LogicBlockMap.FirstOrDefault().Value;
            // position pos = currentcoil.positionMap;
            //pos.X=200;
            //position coilpos = GetOutputStage(currentcoil, St);
            position coilpos = GetLastNetworkSeperator(currentcoil, St);
            coilpos = DrawNetworkComment(currentcoil, ref St, coilpos, ref ids);
            int inputcount = 0;

            position Blockpos = CheckPostionByStage(curblock, coilpos, ref St);
            if(curblock.instanceName== "TDE_I_EFEEDSD")
            {

            }
            int difx = setblockincenterSheetB(int.Parse(Blockpos.X), St);
            if (curblock.typeName.Contains("FB04") || !string.IsNullOrEmpty(curblock.instanceName))
            {
                ChangeBlockPosition(ref curblock, Blockpos, difx, 5);//need updated
            }
            else
            {
                ChangeBlockPosition(ref curblock, Blockpos, difx, 3);//need updated
            }

            ChangeCoilPostion(ref currentcoil, coilpos);
            ConnectCoilToBlok(curblock, ref currentcoil);
            updateEnYforstages(curblock, currentcoil, ref St);
         
            //  foreach (var invar in curblock.inputVariablesMap.variableMap.Values.OrderBy(x => x.connectionPointInMap.connectionMap.formalParameter))
            foreach (var invar in curblock.inputVariablesMap.variableMap.Values)//.OrderBy(x => x.connectionPointInMap.connectionMap.formalParameter))
            {
                string formalparm = invar.connectionPointInMap.connectionMap.formalParameter;
                string RefID = invar.connectionPointInMap.connectionMap.refLocalId;
                int difspace = inputcount * 2;
                if (formalparm != "" && formalparm != null)
                {
                    DrawLogicBlock(ref curblock, ref currentcoil, invar.formalParameter, ref St,mismatches);
                }
                else
                {
                    string modRefID = RefID.Replace(")", "");
                    contact con = SearchContactByName(currentcoil, modRefID);
                    if (con != null)
                    {
                        con.Calculated = "Done";
                        variable myvar = invar;
                        XMLOperations_LadderLogic.ChangeReIDVariable(ref myvar, con.localId, formalparm);
                        if (curblock.typeName != "NOT" && curblock.typeName != "FB04_DI")
                        {
                            XMLOperations_LadderLogic.ChangeNegated(ref myvar, ref curblock, con.typeName);
                        }
                        else
                        {
                        }

                        position invpos1 = XMLOperations_LadderLogic.GetPostionParmeter(curblock, invar.formalParameter, 0, 0);
                        position pos1 = CheckContactPostion(invpos1, ref St);
                        ChangeContactPostion(ref con, pos1);
                        position outcontactpos = GetContactOutPointPosition(con);
                        ConnectTwoBlock(ref myvar, invpos1, outcontactpos, SpaceXDifForlineConnection - difspace);//it was 20 for template d may error
                    }
                    else
                    {
                        inOutVariable cons = searchConstantBYID(currentcoil, modRefID);
                        if (cons != null)
                        {
                            variable myvar = invar;
                            position invpos1 = XMLOperations_LadderLogic.GetPostionParmeter(curblock, invar.formalParameter, 0, 0);
                            position constantpostion = ChangePostionPoint(invpos1, -(int.Parse(cons.width) + 1), -1);
                            changeIovariablepostion(ref cons, constantpostion);
                        }
                        else
                        {
                        }
                    }
                }
                inputcount++;
            }
        }

        private static void updateEnYforstages(SafetyBlock curblock, coil currentcoil, ref SafetyTemplate st)
        {
            int coilheight = 10;
           if( currentcoil.height!= null)
            {
                 int.TryParse(currentcoil.height, out coilheight);
            }
            int endy1 = (int.Parse(currentcoil.positionMap.Y) + coilheight);

            int blockheight = 10;
            if (curblock.height != null)
            {
                int.TryParse(curblock.height, out blockheight);
            }
            int endy2 = (int.Parse(curblock.positionMap.Y) + blockheight);


            int endY = endy2;
            if(endy1> endy2)
            {
                endY = endy1;
            }
            ChangeInputAndOutputStagesEndY(endY.ToString(), ref st);
        }

        private static position DrawNetworkComment(coil currentcoil, ref SafetyTemplate st, position currentpos, ref SortedDictionary<int, int> ids)
        {
            position mycommentpos = new position(currentpos);
            position mycoilpos = new position(currentpos);
            int dify = 4;
            if (currentcoil.NetworkDescription != null && currentcoil.NetworkDescription.Count() >= 1)
            {
                Comment mycomment = XMLOperations_LadderLogic.newComment(st.SheetNumber);

                string newID = (ids.ElementAt(ids.Count() - 1).Value + 1).ToString();
                ids.Add(int.Parse(newID), int.Parse(newID));
                XMLManager.ModifyCommentID(ref mycomment, newID);
                mycommentpos.X = Comment_X_begin;
                mycommentpos = ChangePostionPoint(mycommentpos, 0, -dify);//no need to add space
                ModifycommentPosition(mycomment, mycommentpos);
                int commenthieght = ModifyCommentFromListString(currentcoil.NetworkDescription, ref mycomment);
                currentcoil.MyLogic.CommentMap.Add(mycomment.localId, mycomment);
                currentpos = ChangePostionPoint(currentpos, 0, commenthieght + 2);
            }
            return currentpos;
        }

        private static int ModifyCommentFromListString(List<string> networkDescription, ref Comment mycomment)
        {
            string mystring = GetonestringfromList(networkDescription);
            //int commenthieght = (networkDescription.Count() * 4);//nesma
            int commenthieght = (networkDescription.Count()==1? networkDescription.Count() * 4:networkDescription.Count() * 3);//nesma
            XMLOperations_safety.ModifyCommentHieght(ref mycomment, commenthieght);
            XMLOperations_safety.ModifyCommentContent(ref mycomment, mystring);
            return commenthieght;
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

        private static void ModifycommentPosition(Comment mycomment, position mypos)
        {
            XElement xmlSafetycomment = mycomment.MyXML;

            var postionlist = from S in xmlSafetycomment.Descendants()
                              where S.Name.LocalName == "position" && S.ToString().Contains(":SheetNumber")
                              select S;
            XElement thispostion = postionlist.First();

            if (thispostion != null)
            {
                thispostion.Attribute("x").Value = mypos.X;

                thispostion.Attribute("y").Value = mypos.Y;
            }
        }

        private static void changeIovariablepostion(ref inOutVariable con, position mypostion)
        {
            con.positionMap.X = mypostion.X;
            con.positionMap.Y = mypostion.Y;

            con.positionMap.MyXML.Attribute("x").Value = mypostion.X;
            con.positionMap.MyXML.Attribute("y").Value = mypostion.Y;
            var poxxml = (from b in con.MyXML.Descendants()
                          where b.Name.LocalName == "position"
                          select b).FirstOrDefault();
            if (poxxml != null)
            {
                poxxml.Attribute("x").Value = con.positionMap.X;
                poxxml.Attribute("y").Value = con.positionMap.Y;
            }
        }

        private static inOutVariable searchConstantBYID(coil currentcoil, string modRefID)
        {
            inOutVariable constant = (from S in currentcoil.MyLogic.inOutVariableMap.Values
                                      where S.localId == modRefID
                                      select S).FirstOrDefault();

            return constant;
        }
        //template b
        private static int setblockincenterSheet(int v, SafetyTemplate st)
        {
            int halfcounttest= (st.StageMap.Where(x => x.Value.Type == "Logic").Count()-1) / 2;
            int halfcount = st.StageMap.Where(x => x.Value.Type == "Logic").Count() / 2;
           // halfcount = (halfcount == halfcounttest) ? halfcount + 1 : halfcount;//add for template b
            int count = halfcount * 2;
            int diff = ((count - 1) * Stagediff) / 2;
            //return ((int)(161 - ((BlockWidth * halfcount)+ diff)));//need modify
            // return ((int)(95 - ((BlockWidth * halfcount) + diff)));//need modify
            return ((int)(PageCenterD - ((BlockWidth * halfcount) + diff)));//need modify
        }

        private static int setblockincenterSheetB(int v, SafetyTemplate st)
        {
            double halfcounttest = (st.StageMap.Where(x => x.Value.Type == "Logic").Count() - 1) / 2;
            double halfcount = st.StageMap.Where(x => x.Value.Type == "Logic").Count() / 2;
            // halfcount = (halfcount == halfcounttest) ? halfcount + 1 : halfcount;//add for template b
            double count = halfcount * 2;
            double diff = ((count - 1) * Stagediff) / 2;
            //return ((int)(161 - ((BlockWidth * halfcount)+ diff)));//need modify
            // return ((int)(95 - ((BlockWidth * halfcount) + diff)));//need modify
            return ((int)(PageCenterD - ((BlockWidth * halfcount) + diff)));//need modify
        }
        private static void ChangeCoilPostion(ref coil currentcoil, position coilpos)
        {
            currentcoil.positionMap.X = coilpos.X;
            currentcoil.positionMap.Y = coilpos.Y;
            currentcoil.positionMap.MyXML.Attribute("x").Value = coilpos.X;
            currentcoil.positionMap.MyXML.Attribute("y").Value = coilpos.Y;
            var poxxml = (from b in currentcoil.MyXML.Descendants()
                          where b.Name.LocalName == "position"
                          select b).FirstOrDefault();
            if (poxxml != null)
            {
                poxxml.Attribute("x").Value = currentcoil.positionMap.X;
                poxxml.Attribute("y").Value = currentcoil.positionMap.Y;
            }
        }

        private static void ConnectCoilToBlok(SafetyBlock curblock, ref coil currentcoil)
        {
            string foformalParameter = currentcoil.connectionPointInMap.connectionMap.FirstOrDefault().formalParameter;
            if (foformalParameter.Contains("FB04"))
            {
                foformalParameter = "STATUS_DCS";
            }
            position outparm = XMLOperations_LadderLogic.GetPostionParmeter(curblock, foformalParameter, 0, 0);
            relPosition rel = currentcoil.connectionPointInMap.relPositionMap.FirstOrDefault();
            position coilin = ChangePostionPoint(currentcoil.positionMap, int.Parse(rel.X), int.Parse(rel.Y));
            connection myvar = currentcoil.connectionPointInMap.connectionMap.FirstOrDefault();
            List<position> myList = GetconnectionPointList(coilin, outparm, 0);

            AddpostionListToConnection(ref currentcoil, myList);
        }

        private static void AddpostionListToConnection(ref coil currentcoil, List<position> myList)
        {
            var myconn = currentcoil.connectionPointInMap.connectionMap.FirstOrDefault();
            if (myconn != null)
            {
                myconn.positionMap = new List<position>();
                myconn.MyXML.RemoveNodes();
            }

            var poxxml = (from b in currentcoil.MyXML.Descendants()
                          where b.Name.LocalName == "connection"
                          select b).FirstOrDefault();
            if (poxxml != null)
            {
                poxxml.RemoveNodes();
            }

            foreach (position mypos in myList)
            {
                currentcoil.connectionPointInMap.connectionMap.FirstOrDefault().positionMap.Add(mypos);
                // currentcoil.connectionPointInMap.connectionMap.FirstOrDefault().MyXML.Add(mypos.MyXML);
                poxxml.Add(mypos.MyXML);
            }
        }

        private static position GetOutputStage(coil currentcoil, SafetyTemplate st)
        {
            position mypos = new position(currentcoil.positionMap);
            Stage mystage = st.StageMap.Values.Where(x => x.Type == "Output").OrderByDescending(x => x.EndYdirection).FirstOrDefault();

            mypos.X = mystage.Starting.X;
            if (mypos.Y == "0")
            {
                if (mystage.EndYdirection != 1 && mystage.EndYdirection != 0)
                {
                    mypos.Y = (mystage.EndYdirection + 3).ToString();//3 for instance blocks adjust
                }
                else
                {
                    mypos.Y = "5";
                }
            }
            return mypos;
        }

        private static position GetLastNetworkSeperator(coil currentcoil, SafetyTemplate st)
        {
            var sheet = st.MyXMLSheet;
            var inpList = (from e in sheet.Descendants()
                           where e.Name.LocalName == "NETWORKDIVIDER"
                           select e).ToList();
            string Y = "5";
            XElement thisinpvar;
            if (inpList.Count > 0)
            {
                thisinpvar = inpList.LastOrDefault();
                //  thisinpvar.RemoveNodes();
                Y = thisinpvar.Attribute("Y").Value;
            }
            position mypos = new position(currentcoil.positionMap);
            Stage mystage = st.StageMap.Values.Where(x => x.Type == "Output").OrderByDescending(x => x.EndYdirection).FirstOrDefault();

            mypos.X = (int.Parse(mystage.Starting.X)+1).ToString();
            mypos.Y = (int.Parse(Y) + 8).ToString();

            return mypos;
        }

        private static void ChangeContactPostion(ref contact con, position pos1)
        {
            con.positionMap.X = pos1.X;
            con.positionMap.Y = pos1.Y;

            con.positionMap.MyXML.Attribute("x").Value = pos1.X;
            con.positionMap.MyXML.Attribute("y").Value = pos1.Y;
            var poxxml = (from b in con.MyXML.Descendants()
                          where b.Name.LocalName == "position"
                          select b).FirstOrDefault();
            if (poxxml != null)
            {
                poxxml.Attribute("x").Value = con.positionMap.X;
                poxxml.Attribute("y").Value = con.positionMap.Y;
            }
        }

        private static position CheckContactPostion(position pos1, ref SafetyTemplate st)
        {
            int max = 0;
            int min = 0;
            int relY = 1;
            position mypos = new position(pos1);
            Stage selectedStage = new Stage();
            var myinpminstage = st.StageMap.Values.Where(x => x.Type == "Input").OrderBy(x => x.EndYdirection).FirstOrDefault();
            var mymaxinpstage = st.StageMap.Values.Where(x => x.Type == "Input").OrderByDescending(x => x.EndYdirection).FirstOrDefault();
            if (mymaxinpstage != null)
            {
                max = mymaxinpstage.EndYdirection;
            }
            if (myinpminstage != null)
            {
                min = myinpminstage.EndYdirection;
            }
            int myposY = int.Parse(mypos.Y) - relY;
            mypos.Y = myposY.ToString();
            if (myposY > max)
            {
                selectedStage = st.StageMap.Values.Where(x => x.Type == "Input").OrderByDescending(x => x.Order).FirstOrDefault();
            }
            else
            {
                selectedStage = myinpminstage;
            }
            mypos.X = (int.Parse(selectedStage.Starting.X) + 1).ToString();
            if (selectedStage.EndYdirection >= int.Parse(mypos.Y))
            {
                mypos.Y = (selectedStage.EndYdirection + 2).ToString();
                selectedStage.EndYdirection = selectedStage.EndYdirection + selectedStage.DifferenceY;
            }
            else
            {
                if (myposY == max)
                {
                    mypos.Y = (max + 1).ToString();
                }
                selectedStage.EndYdirection = int.Parse(mypos.Y) + selectedStage.DifferenceY;
            }

            if ((int.Parse(mypos.Y) == max - selectedStage.DifferenceY) && (int.Parse(mypos.Y) < max) && (selectedStage.Order == -2))
            {
                mypos.Y = (int.Parse(mypos.Y) + 1).ToString();
                selectedStage.EndYdirection = int.Parse(mypos.Y) + selectedStage.DifferenceY;
            }

            if ((int.Parse(mypos.Y) >= max - selectedStage.DifferenceY) && (int.Parse(mypos.Y) < max) && (selectedStage.Order == -1))
            {
                mypos.Y = (max + 2).ToString();
                selectedStage.EndYdirection = int.Parse(mypos.Y) + selectedStage.DifferenceY;
            }
            ChangeOutStagesEndY(mypos.Y, ref st);
            return mypos;
        }

        private static void ChangeOutStagesEndY(string myposY, ref SafetyTemplate st)
        {
            foreach (Stage mystage in st.StageMap.Values.Where(t => t.Type == "Output"))
            {
                mystage.EndYdirection = int.Parse(myposY) + StageYdiff; //mystage.DifferenceY + Networkdiff;
            }
        }

        private static void ChangeInputAndOutputStagesEndY(string myposY, ref SafetyTemplate st)
        {
            foreach (Stage mystage in st.StageMap.Values.Where(t => t.Type == "Output"))
            {
                if (mystage.EndYdirection < int.Parse(myposY))
                {
                    mystage.EndYdirection = int.Parse(myposY)+ StageYdiff;//+ mystage.DifferenceY + Networkdiff ;
                }
            }
            //foreach (Stage mystage in st.StageMap.Values.Where(t => t.Type == "Input"))
            //{
            //    if(mystage.EndYdirection < int.Parse(myposY))
            //    {
            //        mystage.EndYdirection = int.Parse(myposY) + mystage.DifferenceY + Networkdiff;
            //    }

            //    else
            //    {
            //       // ChangeOutStagesEndY(myposY, ref st);
            //    }
            //}
        }

        private static position CheckPostionByStage(SafetyBlock curblock, position pos, ref SafetyTemplate st)
        {
            position mypostion = new position(pos);
            var mystage = st.StageMap.FirstOrDefault(x => x.Value.Order == curblock.Stage).Value;
            if (mystage != null)
            {
                int blockhight = CalculateHight(curblock);
                int comparingvalue = mystage.EndYdirection;
                if (CheckhasContactconnection(curblock))
                {
                    comparingvalue = SearchEndYinafterStages(mystage.EndYdirection, curblock.Stage, st);
                }
                if (comparingvalue > int.Parse(pos.Y))
                {
                    mypostion.Y = (comparingvalue + mystage.DifferenceY).ToString();
                    mystage.EndYdirection = int.Parse(mypostion.Y) + blockhight;
                }
                else
                {
                    mystage.EndYdirection = int.Parse(mypostion.Y) + blockhight;
                }
            }

            return mypostion;
        }

        private static int SearchEndYinafterStages(int currentEnd, int myorder, SafetyTemplate st)
        {
            int returnvalue = currentEnd;
            var stages = (from S in st.StageMap.Values
                          where S.EndYdirection > currentEnd && S.Order > myorder && S.Type != "Output"
                          select S);
            foreach (var stage in stages)
            {
                if (stage.EndYdirection > returnvalue)
                {
                    returnvalue = stage.EndYdirection;
                }
            }

            return returnvalue;
        }

        private static bool CheckhasContactconnection(SafetyBlock curblock)
        {
            bool returnvalue = false;
            var myvar = (from s in curblock.inputVariablesMap.variableMap.Values
                             //   where s.connectionPointInMap.connectionMap.formalParameter == ""
                         where (s.connectionPointInMap.connectionMap.formalParameter == "" || !String.IsNullOrEmpty(s.connectionPointInMap.connectionMap.refLocalId))
                         select s).FirstOrDefault();
            if (myvar != null)
            {
                returnvalue = true;
            }
            return returnvalue;
        }

        private static int CalculateHight(SafetyBlock curblock)
        {
            //int hight = ((curblock.inputVariablesMap.variableMap.Count() - 1) * 2) + 8;//nesma
            int hight = ((curblock.inputVariablesMap.variableMap.Count() - 1) * 2) + (CoilContactHeight- BlocksYdifferenceTunning);
            return hight;
        }

        private static void DrawLogicBlock(ref SafetyBlock inblock, ref coil currentcoil, string invarname, ref SafetyTemplate St, List<string> mismatches)
        {
            var myvar = inblock.inputVariablesMap.variableMap.FirstOrDefault(x => x.Value.formalParameter == invarname).Value;
            var myvawkeyr = inblock.inputVariablesMap.variableMap.FirstOrDefault(x => x.Value.formalParameter == invarname);
            string formalparm = myvar.connectionPointInMap.connectionMap.formalParameter;

            int index = inblock.inputVariablesMap.variableMap.ToList().IndexOf(myvawkeyr);
            int difspaceforvariable = index * 2;
            int difspaceforblock = index * 2;
            string RefID = myvar.connectionPointInMap.connectionMap.refLocalId;
            if (!string.IsNullOrEmpty(formalparm))
            {
                SafetyBlock outblock = currentcoil.MyLogic.LogicBlockMap.FirstOrDefault(x => x.Value.localId == RefID).Value;
                if (outblock != null)
                {
                    int ydiff = 0;
                    int xdifff = -10;
                    if (invarname == "IN1")
                    {
                        ydiff = 0;
                    }
                    position invpos = XMLOperations_LadderLogic.GetPostionParmeter(inblock, invarname, 0, 0);

                    position pos = ChangePostionPoint(invpos, -10, ydiff);
                    pos = CheckPostionByStage(outblock, pos, ref St);
                    if (index != 0)
                    {
                        ChangeBlockPosition(ref outblock, pos, SpaceXDifForBlocks, 0);//templatez
                    }

                    if (!string.IsNullOrEmpty(outblock.instanceName) && index == 0)
                    {
                        ChangeBlockPosition(ref outblock, pos, SpaceXDifForBlocks, 6);//need updated
                    }
                    if (string.IsNullOrEmpty(outblock.instanceName) && index == 0)
                    {
                        ChangeBlockPosition(ref outblock, pos, SpaceXDifForBlocks, 4);//need updated
                    }

                    position OutPosition = XMLOperations_LadderLogic.GetPostionParmeter(outblock, formalparm, 0, 0);

                    // foreach (var invar in outblock.inputVariablesMap.variableMap.Values.OrderBy(x => x.connectionPointInMap.connectionMap.formalParameter))
                    foreach (var invar in outblock.inputVariablesMap.variableMap.Values)
                    {
                        DrawLogicBlock(ref outblock, ref currentcoil, invar.formalParameter, ref St,mismatches);
                    }
                    if (invarname == "IN4")
                    {
                    }

                    ConnectTwoBlock(ref myvar, invpos, OutPosition, SpaceXDifForlineConnection - difspaceforblock);//need update if required
                }
            }
            else
            {
                RefID = RefID.Replace(")", "");
                contact mycontact = SearchContactByName(currentcoil, RefID);
                if (mycontact != null)
                {
                    mycontact.Calculated = "Done";
                    XMLOperations_LadderLogic.ChangeReIDVariable(ref myvar, mycontact.localId, formalparm);
                    if (inblock.typeName != "NOT")
                    {
                        XMLOperations_LadderLogic.ChangeNegated(ref myvar, ref inblock, mycontact.typeName);
                    }
                    else
                    {
                    }

                    position invpos1 = XMLOperations_LadderLogic.GetPostionParmeter(inblock, myvar.formalParameter, 0, 0);
                    position pos1 = CheckContactPostion(invpos1, ref St);
                    ChangeContactPostion(ref mycontact, pos1);
                    position outcontactpos = GetContactOutPointPosition(mycontact);
                    //ConnectTwoBlock(ref myvar, invpos1, outcontactpos,20-difspace);//this for not D template
                    ConnectTwoBlock(ref myvar, invpos1, outcontactpos, SpaceXDifForlineConnection - difspaceforvariable);//this for D template
                }
                else
                {
                    inOutVariable cons = searchConstantBYID(currentcoil, RefID);
                    if (cons != null)
                    {
                        //variable myv = myvar;
                        position invpos1 = XMLOperations_LadderLogic.GetPostionParmeter(inblock, myvar.formalParameter, 0, 0);
                        position constantpostion = ChangePostionPoint(invpos1, -(int.Parse(cons.width) + 1), -1);
                        changeIovariablepostion(ref cons, constantpostion);
                    }
                    else
                    {
                        string mis = "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work";

                        if (!mismatches.Contains(mis))
                        {
                            mismatches.Add(mis);
                        }

                        //string Status ="Network "+ RefID + " in logic of " + currentcoil.Name + "not converted need manual work";
                        // mismatches.Add(Status);
                        //  BL.Logger.LogError(null, Status);
                        XMLOperations_LadderLogic.ChangeReIDVariable(ref myvar, "0", formalparm);
                    }
                }
            }
        }

        private static position GetContactOutPointPosition(contact mycontact)
        {
            relPosition myrel = mycontact.connectionPointOutMap.relPositionMap.FirstOrDefault();
            if (myrel == null)
            {
                if (mycontact.width != null)
                {
                    int w = int.Parse(mycontact.width) + 1;
                    myrel = new relPosition(w.ToString(), "1");//need update
                }
                else
                {
                    myrel = new relPosition((CoilContactWidth+1).ToString(), "1");//need update
                }
            }

            position mypos = ChangePostionPoint(mycontact.positionMap, int.Parse(myrel.X), int.Parse(myrel.Y));
            return mypos;
        }

        private static void ConnectTwoBlock(ref variable myvar, position invpos, position outPosition, int Xdiff)
        {
            List<position> myList = GetconnectionPointList(invpos, outPosition, Xdiff);

            AddpostionListToVar(ref myvar, myList);
        }

        private static List<position> GetconnectionPointList(position invpos, position outPosition, int Xdiff)
        {
            List<position> myList = new List<position>();
            myList.Add(invpos);
            if (invpos.Y == outPosition.Y)
            {
                myList.Add(outPosition);
            }
            else
            {
                string decpacX = (int.Parse(invpos.X) - Xdiff).ToString();
                position sameY = new position(decpacX, invpos.Y);
                myList.Add(sameY);
                position samX = new position(decpacX, outPosition.Y);
                myList.Add(samX);
                myList.Add(outPosition);
            }

            return myList;
        }

        private static void AddpostionListToVar(ref variable myvar, List<position> myList)
        {
            myvar.connectionPointInMap.connectionMap.positionMap = new List<position>();
            myvar.connectionPointInMap.connectionMap.MyXML.RemoveNodes();
            var poxxml = (from b in myvar.MyXML.Descendants()
                          where b.Name.LocalName == "connection"
                          select b).FirstOrDefault();
            poxxml.RemoveNodes();
            foreach (position mypos in myList)
            {
                myvar.connectionPointInMap.connectionMap.positionMap.Add(mypos);
                myvar.connectionPointInMap.connectionMap.MyXML.Add(mypos.MyXML);
                poxxml.Add(mypos.MyXML);
            }
        }

        private static position ChangePostionPoint(position pos, int xdiff, int ydiff)
        {
            position mypos = new position();
            mypos.X = (int.Parse(pos.X) + xdiff).ToString();
            mypos.Y = (int.Parse(pos.Y) + ydiff).ToString();
            mypos.MyXML.Attribute("x").Value = mypos.X;
            mypos.MyXML.Attribute("y").Value = mypos.Y;
            return mypos;
        }

        private static contact SearchContactByName(coil currentcoil, string Name)
        {
            contact coil = (from S in currentcoil.MyLogic.LogiccontactMap.Values
                            where S.Name == Name && S.Calculated == "Not Done"
                            select S).FirstOrDefault();

            return coil;
        }

        private static void ChangeBlockPosition(ref SafetyBlock curblock, position pos, int relX, int relY)
        {
            curblock.positionMap.X = (int.Parse(pos.X) - relX).ToString();
            curblock.positionMap.Y = (int.Parse(pos.Y) - relY).ToString();
            var poxxml = (from b in curblock.MyXML.Descendants()
                          where b.Name.LocalName == "position"
                          select b).FirstOrDefault();
            if (poxxml != null)
            {
                poxxml.Attribute("x").Value = curblock.positionMap.X;
                poxxml.Attribute("y").Value = curblock.positionMap.Y;
            }
        }

        private static void ReadblockinputLogic(ref SafetyBlock motherBlock, List<string> mylogic, ref coil st, ref int stage, ref SortedDictionary<int, int> IDs, List<string> mismatches)
        {
            int inpcount = 1;

            for (int logindx = 0; logindx < mylogic.Count(); logindx++)
            {
                string inp = mylogic[logindx].Replace("(", "").Replace(")","");//.ToUpper();//CHECK THIS
                                                                               // if (inp.Contains("And") || inp.Contains("OR"))
                if (motherBlock.typeName == "PERDEV")
                {

                }

                if ((inp.ToUpper().Contains("AND")&& inp.Length==3 )|| inp == "OR" || inp.Contains("TRIG") || inp=="GT"|| inp.Contains("TDD_I") || inp.Contains("TDE_I") || inp=="TON" || inp == "TOF" || inp.Replace("(", "") == "GE" || 
                    inp == "LE" || inp == "LT"|| inp == "EQ"|| inp == "NE"|| inp=="SR" || inp == "RS" || inp == "TMR_I" || inp == "CTU" || inp.Replace("(", "") == "TP_I"|| inp.Replace("(", "") == "TP")
                {
                    List<string> newlogic = GetCompletelogic(mylogic, logindx);
                    string blocktype = inp.Replace("(", "").ToUpper();
                    // SafetyBlock newBlock = XMLOperations_LadderLogic.newBlockType(blocktype,st.positionMap.SheetNumber);
                    SafetyBlock newBlock = XMLOperations_LadderLogic.newBlockType(blocktype, motherBlock.positionMap.SheetNumber);

                    string newID = (IDs.ElementAt(IDs.Count() - 1).Value + 1).ToString();
                    IDs.Add(int.Parse(newID), int.Parse(newID));

                    XMLOperations_LadderLogic.ChangeBlockID(ref newBlock, newID);

                    newBlock.Logic = newlogic;
                    stage++;
                    newBlock.Stage = stage;
                    if (newlogic.Count() <= 0)
                    {
                    }
                    newlogic.RemoveAt(0);

                   
                    newlogic[newlogic.Count() - 1] = newlogic[newlogic.Count() - 1].Replace("))", ")");
                  
                    logindx = logindx + newlogic.Count();
                    string outparametername = Getoutparameter(blocktype);
                    XMLOperations_LadderLogic.AddInputvariableToBlock(ref motherBlock, newBlock.localId, outparametername, inpcount);
                    st.MyLogic.LogicBlockMap.Add(newBlock.localId, newBlock);
                    ReadblockinputLogic(ref newBlock, newlogic, ref st, ref stage, ref IDs,mismatches);

                    inpcount++;
                }
                else
                {
                    //variable newvar = new variable();
                    //newvar.formalParameter = "IN" + inpcount.ToString();
                    //newvar.connectionPointInMap.connectionMap.refContactName = inp;
                    //motherBlock.inputVariablesMap.variableMap.Add(newvar.formalParameter, newvar);
                    inp = inp.Replace(")", "");
                    contact mycon = SearchContactByName(st, inp);
                    if (mycon != null)
                    {
                        XMLOperations_LadderLogic.AddInputvariableToBlock(ref motherBlock, inp, "", inpcount);
                        inpcount++;
                   
                        if (mylogic.Count() == 1 && (mycon.typeName == "/"|| st.Coiltype=="NOT"))
                        {
                            XMLOperations_LadderLogic.ChangeBlockType(ref motherBlock, "NOT");
                        }

                        if (!string.IsNullOrEmpty(motherBlock.instanceName) && !motherBlock.typeName.Contains("FB04_DI"))
                        {
                            Getnameforblock(motherBlock, mycon);
                        }
                    }
                    else
                    {
                        if (inp.Contains("#") || motherBlock.typeName == "GE" || motherBlock.typeName == "GT" || motherBlock.typeName == "LE" || motherBlock.typeName == "LT"|| motherBlock.typeName == "TDD_I"|| motherBlock.typeName == "TDE_I"
                            || motherBlock.typeName == "SCALE" || motherBlock.typeName == "AIN" || motherBlock.typeName == "TCJ_CONV" || motherBlock.typeName.Contains("STFB") || motherBlock.typeName == "MOVE"|| motherBlock.typeName == "PERDEV" ||
                            motherBlock.typeName == "EQ"|| motherBlock.typeName == "NE"|| motherBlock.typeName == "TP"|| motherBlock.typeName == "TP_I" || motherBlock.typeName == "TMR_I"|| motherBlock.typeName == "CTU")
                        {
                            inOutVariable newcons;
                            if (inp.Contains("#"))
                            {
                                newcons = XMLOperations_LadderLogic.newconstant("TIME", motherBlock.positionMap.SheetNumber);
                                XMLOperations_safety.ModifyIOvariableName(ref newcons, newcons.expressionMap.Tagname, inp);
                            }
                            else
                            {
                             
                                string typeinp= gettype(inp);
                                if (motherBlock.typeName == "AIN" || motherBlock.typeName == "SCALE" || motherBlock.typeName == "TCJ_CONV"|| motherBlock.typeName == "PERDEV")
                                {
                                    newcons = XMLOperations_LadderLogic.newconstant("REAL", motherBlock.positionMap.SheetNumber);
                                   // newcons = XMLOperations_LadderLogic.newconstant(typeinp, motherBlock.positionMap.SheetNumber);
                                }
                                else
                                {
                                     newcons = XMLOperations_LadderLogic.newconstant("DINT", motherBlock.positionMap.SheetNumber);
                                   // newcons = XMLOperations_LadderLogic.newconstant(typeinp, motherBlock.positionMap.SheetNumber);
                                }
                                if (motherBlock.typeName.Contains("STFB") || motherBlock.typeName == "MOVE"|| motherBlock.typeName.Contains("TDD")|| motherBlock.typeName.Contains("TDE")|| motherBlock.typeName.Contains("TP"))
                                {
                                    string type = gettype(inp);
                                    newcons = XMLOperations_LadderLogic.newconstant(type, motherBlock.positionMap.SheetNumber);
                                }

                                Double val = 0;
                             

                                if (double.TryParse(inp, out val) || (newcons !=null && newcons.TagAttributesMap.DATATYPE == "BOOL")|| typeinp=="BOOL")
                                {
                                    XMLOperations_safety.ModifyIOvariableName(ref newcons, newcons.expressionMap.Tagname, inp);
                                }
                            }

                            string newID = (IDs.ElementAt(IDs.Count() - 1).Value + 1).ToString();
                            IDs.Add(int.Parse(newID), int.Parse(newID));

                            XMLOperations_safety.ModifyIOvariableID(ref newcons, newID);
                            if(newcons==null)
                            {

                            }
                            st.MyLogic.inOutVariableMap.Add(newcons.localId, newcons);
                            XMLOperations_LadderLogic.AddInputvariableToBlock(ref motherBlock, newID, "", inpcount);
                            inpcount++;
                        }
                    }
                }
            }
            CheckoneInputBlock(motherBlock, inpcount, mismatches,st);
            stage--;
        }

        private static void CheckoneInputBlock(SafetyBlock motherBlock, int inpcount, List<string> mismatches,coil currentcoil)
        {
            if(inpcount==2&& !CheckAcceptedoneInputBlock(motherBlock))
            {

                // string mis = "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work,there is Block not connected";
                string mis = "Network # " + currentcoil.Networknumber + ": can't be converted:this network need manual work";
                if (!mismatches.Contains(mis))
                {
                    mismatches.Add(mis);
                }
            }
        }

        private static bool CheckAcceptedoneInputBlock(SafetyBlock motherBlock)
        {
            string blocktype = motherBlock.typeName;
            return blocktype == "MOVE" || blocktype == "NOT"|| blocktype.Contains("_TRIG");
        }

        private static void Getnameforblock(SafetyBlock motherBlock, contact mycon)
        {
            int i = 1;
            string blockname = motherBlock.typeName + "_" + mycon.Name + "_" + i.ToString();
            while (blocknames.Exists(x => x == blockname))
            {
                i++;
                blockname = motherBlock.typeName + "_" + mycon.Name + "_" + i.ToString();
            }
            blocknames.Add(blockname);
            XMLOperations_safety.ModifyBlockName(blockname, motherBlock);
        }

        private static string gettype(string value)
        {
            string modType = "";
            double intvalue;
            if (double.TryParse(value, out intvalue))
            {
                if (value.Contains("."))
                {
                    modType = "REAL";
                }
                else
                {
                    modType = "DINT";
                }
            }

            if (value == "1" || value == "0" || value.ToUpper() == "TRUE"|| value.ToUpper() == "FALSE")
            {
                modType = "BOOL";
            }

            return modType;
        }

        private static List<string> GetCompletelogic(List<string> mylogic, int v)
        {
            int stage = 1;
            int start = 1;
            List<string> logic = new List<string>();
            for (int k = v; k < mylogic.Count(); k++)
            {
                logic.Add(mylogic[k]);
                if (mylogic[k].Contains("("))
                {
                    stage++;
                }
                if (mylogic[k].Contains(")"))
                {
                    string[] countend = mylogic[k].Split(')');
                    stage = stage - (countend.Length - 1);
                    if (stage == start)
                    {
                        break;
                    }
                }
            }
            return logic;
        }

        internal static coil Deserializecoil(XElement thiscoil)
        {
            coil newcoil = new coil();

            newcoil.localId = thiscoil.Attribute("localId").Value;
            newcoil.width = thiscoil.Attribute("width").Value;
            newcoil.height = thiscoil.Attribute("height").Value;
            newcoil.MyXML = thiscoil;

            var Attributes =
                             from attr in thiscoil.Attributes()
                             where attr.Name.ToString().Contains("storage")
                             select attr;
            foreach (var attribute in Attributes)
            {
                newcoil.storage = attribute.Value;
            }

            var posList = (from x in thiscoil.Descendants()
                           where x.Name.LocalName == "position"
                           select x).ToList();

            XElement thisPosition = posList[0];

            if (thisPosition.ToString().Contains("SheetNumber"))
            {
                newcoil.positionMap.X = thisPosition.Attribute("x").Value;
                newcoil.positionMap.Y = thisPosition.Attribute("y").Value;
                newcoil.positionMap.MyXML = thisPosition;

                var nameAttributes =
                                 from attr in thisPosition.Attributes()
                                 where attr.Name.ToString().Contains("SheetNumber")
                                 select attr;
                foreach (var attribute in nameAttributes)
                {
                    newcoil.positionMap.SheetNumber = attribute.Value;
                }
            }

            var inpList = (from e in thiscoil.Descendants()
                           where e.Name.LocalName == "connectionPointIn"
                           select e).ToList();

            //foreach (XElement thisinpvar in inpList)

            if (inpList.Count > 0)
            {
                XElement thisinpvar = inpList[0];

                contactconnectionPointIn newinppin = DeserializeconnectionPointIn(thisinpvar);

                newcoil.connectionPointInMap = newinppin;
            }

            //beginoutput
            var oupList = (from N in thiscoil.Descendants()
                           where N.Name.LocalName == "connectionPointOut"
                           select N).ToList();

            if (oupList.Count > 0)

            //foreach (XElement thisoupvar in oupList)
            {
                XElement thisoupvar = oupList[0];

                connectionPointOut newouppin = DeserializeconnectionPointOut(thisoupvar);

                newcoil.connectionPointOutMap = newouppin;
            }

            //expression
            var variablelist = (from N in thiscoil.Descendants()
                                where N.Name.LocalName == "variable"
                                select N).ToList();
            if (variablelist.Count > 0)
            {
                XElement thisvariable = variablelist[0];
                //expression newexpression = new expression();
                //newexpression.Tagname = thisexpression.Value;
                newcoil.Name = thisvariable.Value;
            }

            return newcoil;
        }

        internal static contact Deserializecontact(XElement thiscontact)
        {
            contact newcontact = new contact();

            newcontact.localId = thiscontact.Attribute("localId").Value;
            newcontact.width = thiscontact.Attribute("width").Value;
            newcontact.height = thiscontact.Attribute("height").Value;
            newcontact.MyXML = thiscontact;

            //newcontact.typeName = thiscontact.Attribute("typeName").Value;
            //newcontact.BlockAttributes.BlockType = thiscontact.Attribute("typeName").Value;

            var posList = (from x in thiscontact.Descendants()
                           where x.Name.LocalName == "position"
                           select x).ToList();

            XElement thisPosition = posList[0];

            if (thisPosition.ToString().Contains("SheetNumber"))
            {
                newcontact.positionMap.X = thisPosition.Attribute("x").Value;
                newcontact.positionMap.Y = thisPosition.Attribute("y").Value;
                newcontact.positionMap.MyXML = thisPosition;

                var nameAttributes =
                                 from attr in thisPosition.Attributes()
                                 where attr.Name.ToString().Contains("SheetNumber")
                                 select attr;
                foreach (var attribute in nameAttributes)
                {
                    newcontact.positionMap.SheetNumber = attribute.Value;
                }
            }

            var inpList = (from e in thiscontact.Descendants()
                           where e.Name.LocalName == "connectionPointIn"
                           select e).ToList();

            //foreach (XElement thisinpvar in inpList)

            if (inpList.Count > 0)
            {
                XElement thisinpvar = inpList[0];

                contactconnectionPointIn newinppin = DeserializeconnectionPointIn(thisinpvar);

                newcontact.connectionPointInMap = newinppin;
            }

            //beginoutput
            var oupList = (from N in thiscontact.Descendants()
                           where N.Name.LocalName == "connectionPointOut"
                           select N).ToList();

            if (oupList.Count > 0)

            //foreach (XElement thisoupvar in oupList)
            {
                XElement thisoupvar = oupList[0];

                connectionPointOut newouppin = DeserializeconnectionPointOut(thisoupvar);

                newcontact.connectionPointOutMap = newouppin;
            }

            //expression
            var variablelist = (from N in thiscontact.Descendants()
                                where N.Name.LocalName == "variable"
                                select N).ToList();
            if (variablelist.Count > 0)
            {
                XElement thisvariable = variablelist[0];
                //expression newexpression = new expression();
                //newexpression.Tagname = thisexpression.Value;
                newcontact.Name = thisvariable.Value;
            }

            return newcontact;
        }

        private static connectionPointOut DeserializeconnectionPointOut(XElement thisoupvar)
        {
            connectionPointOut newconnectionPointOut = new connectionPointOut();
            var relPositionlist = from Q in thisoupvar.Descendants()
                                  where Q.Name.LocalName == "relPosition"
                                  select Q;
            foreach (XElement thisrelPosition in relPositionlist)
            {
                relPosition newrelPosition = new relPosition();
                newrelPosition.X = thisrelPosition.Attribute("x").Value;
                newrelPosition.Y = thisrelPosition.Attribute("y").Value;
                newrelPosition.MyXML = thisrelPosition;
                newconnectionPointOut.relPositionMap.Add(newrelPosition);
            }

            return newconnectionPointOut;
        }

        //serlize inoutvariable

        public static inOutVariable DeserializeSafetyinOutVariable(XElement thisIO)
        {
            inOutVariable newinOutVariable = new inOutVariable();

            try
            {
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
            }
            catch (Exception ez)
            {

          
            }

            return newinOutVariable;
        }

        //serlize block
        public static SafetyBlock DeserializeSafetyBlock(XElement thisBlk)
        {
            SafetyBlock newBlk = new SafetyBlock();

            try
            {
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
                    newBlk.positionMap.MyXML = thisPosition;
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
            }
            catch (Exception es)
            {
                var tt = es.Message;
            }

            return newBlk;
        }

        //serlize inoutvariable

        public static Dictionary<string, SafetyTemplate> DeserializeSafetyTemplate(XElement ioFlgList, string ProgramName)
        {
            Dictionary<string, SafetyTemplate> newSafetyTemplateMap = new Dictionary<string, SafetyTemplate>();

            var sheetList = from HH in ioFlgList.Descendants()
                            where HH.Name.LocalName == "SHEET"
                            select HH;

            foreach (XElement thissheet in sheetList)
            {
                SafetyTemplate mynewSafetyTemplate = new SafetyTemplate();
                mynewSafetyTemplate.MyXMLSheet = thissheet;
                string sheetnumber = thissheet.Attribute("Number").Value;

                newSafetyTemplateMap.Add(sheetnumber, mynewSafetyTemplate);

                var macroList = from b in thissheet.Descendants()
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
                            string sheettemplate = thisvalues.Value;
                            newSafetyTemplateMap[sheetnumber].Template = sheettemplate;
                            newSafetyTemplateMap[sheetnumber].ProgramName = ProgramName;
                            newSafetyTemplateMap[sheetnumber].SheetNumber = sheetnumber;
                        }
                    }
                }
            }

            return newSafetyTemplateMap;
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

        public static string GetLogic(ref SafetyTemplate st)
        {
            string logic = "";
            string reftag = "";
            foreach (coil onecoil in st.SafetycoilMap.Values)
            {
                coil mycoil = onecoil;
                if (onecoil.connectionPointInMap.connectionMap.Count > 1)
                {
                    onecoil.Logic = "OR(";
                }
                else
                {
                    onecoil.Logic = "";
                }
                foreach (connection myconnection in onecoil.connectionPointInMap.connectionMap)
                {
                    string refconnection = myconnection.refLocalId;
                    contact mycon = Getcontact(refconnection, st);
                    if (mycon != null)
                    {
                        onecoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
                        getcontactlogic(ref st, ref mycon, ref mycoil);
                        reftag = mycon.Logic;
                        myconnection.afterLogic = mycon.Logic;
                        myconnection.prevLogic = mycon.PrevLogic;
                        myconnection.refContactName = mycon.Name;
                    }
                    else
                    {
                        // reftag = GetNamebyref(refconnection, st);
                        SafetyBlock myoutblock = GetBlokName(refconnection, st);
                        string formalparm = "";
                        if (myconnection.formalParameter != null)
                        {
                            formalparm = myconnection.formalParameter;
                        }
                        if (myoutblock != null)
                        {
                            myconnection.afterLogic = myoutblock.localId;
                            myconnection.prevLogic = myoutblock.localId;
                            myconnection.refContactName = formalparm;
                            onecoil.MyLogic.LogicBlockMap.Add(myoutblock.localId + "@" + formalparm, myoutblock);
                        }
                        else
                        {
                            myconnection.afterLogic = myconnection.refLocalId;
                            myconnection.prevLogic = myconnection.refLocalId;
                            myconnection.refContactName = myconnection.refLocalId;
                        }
                    }

                    //  onecoil.Logic = onecoil.Logic + reftag + "@ ";
                }
                //if (onecoil.Logic.Contains("OR"))
                //    onecoil.Logic = onecoil.Logic + ")";
                //coil mycoil = onecoil;
                classifiedcoilpaths(ref mycoil);
            }

            return logic;
        }

        public static void getcontactlogic(ref SafetyTemplate st, ref contact mycon, ref coil mycoil)
        {
            GetLogicPath(ref st, ref mycon, ref mycoil);
            Oringpaths(ref mycon);
            mycon.Calculated = "Done";
            string myconID = mycon.localId;
            if (mycoil.MyLogic.LogiccontactMap.FirstOrDefault(k => k.Key == myconID).Value == null)
            {
                mycoil.MyLogic.LogiccontactMap.Add(mycon.localId, mycon);
            }
        }

        public static void Oringpaths(ref contact currentcontact)
        {
            //foreach (connection myconnection in currentcontact.connectionPointInMap.connectionMap)
            //{
            //    currentcontact.Logic = currentcontact.Logic + "@" + myconnection.prevLogic;
            //}
            //currentcontact.PrevLogic = "OR(" + currentcontact.Logic + "))";
            //currentcontact.Logic = "And(" + currentcontact.Name + "@" + "OR(" + currentcontact.Logic + "))";
            classifiedContactpaths(ref currentcontact);
        }

        public static void classifiedContactpaths(ref contact currentcontact)
        {
            string total = classifiedpaths(currentcontact.connectionPointInMap.connectionMap);
            currentcontact.PrevLogic = total;
            string modtotal = RemovefirstLogic(total, "And").Replace("@@", "@");
            currentcontact.Logic = "And(" + currentcontact.Name + "@" + modtotal + ")";
            currentcontact.Logic = currentcontact.Logic.Replace("@@", "@");
        }

        public static void classifiedcoilpaths(ref coil currentcontat)
        {
            string total = classifiedpaths(currentcontat.connectionPointInMap.connectionMap);
            currentcontat.Logic = total.Replace("@@", "@");
        }

        private static string classifiedpaths(List<connection> connectionMap)
        {
            Dictionary<string, List<string>> connectionstring = new Dictionary<string, List<string>>();

            foreach (connection myconnection in connectionMap)
            {
                if (connectionstring.Keys.FirstOrDefault(st => st == myconnection.prevLogic) == null)
                {
                    List<string> mylist = new List<string>();
                    if (myconnection.prevLogic == null)
                    {
                    }
                    connectionstring.Add(myconnection.prevLogic, mylist);

                    string contactname = myconnection.refContactName;
                    connectionstring[myconnection.prevLogic].Add(contactname);
                }
                else
                {
                    string contactname = myconnection.refContactName;
                    connectionstring[myconnection.prevLogic].Add(contactname);
                }
            }
            List<string> logics = new List<string>();

            foreach (string key in connectionstring.Keys)
            {
                string oringlogic = "";
                foreach (string logic in connectionstring[key])
                {
                    if (logic != "")
                        oringlogic = oringlogic + "@" + logic;
                }
                if (connectionstring[key].Count > 1)
                {
                    oringlogic = "OR(" + oringlogic + ")";
                }
                string finallogic = "";
                if (oringlogic != "")
                {
                    string modkey = RemovefirstLogic(key, "And");
                    finallogic = "And(" + oringlogic + "@" + modkey + ")";
                }
                else
                    finallogic = key;
                logics.Add(finallogic);
            }
            string total = "";
            if (logics.Count > 1)
            {
                foreach (string par in logics)
                {
                    total = total + "@" + par;
                }

                total = "OR(" + total + ")";
            }
            else
            {
                total = logics[0];
            }
            return total;
        }

        private static string RemovefirstLogic(string key, string logicgate)
        {
            string finallogic = key;
            if (!Information.IsNumeric(key))
            {
                if (key.Substring(0, logicgate.Length) == logicgate)
                {
                    finallogic = key.Substring(logicgate.Length + 1, key.Length - logicgate.Length - 2);
                }
            }
            else
            {
                finallogic = key;
            }
            return finallogic;
        }

        public static void GetLogicPath(ref SafetyTemplate st, ref contact currentcontact, ref coil mycoil)
        {
            foreach (connection myconnection in currentcontact.connectionPointInMap.connectionMap)
            {
                contact mycon = Getcontact(myconnection.refLocalId, st);

                if (mycon != null)
                {
                    if (mycon.Calculated == "Not Done")
                    {
                        getcontactlogic(ref st, ref mycon, ref mycoil);
                    }
                    myconnection.prevLogic = mycon.PrevLogic;
                    myconnection.afterLogic = mycon.Logic;
                    myconnection.refContactName = mycon.Name;
                }
                else
                {
                    myconnection.prevLogic = myconnection.refLocalId;
                    myconnection.afterLogic = myconnection.refLocalId;
                    myconnection.refContactName = "True";
                }
            }
        }

        private static string GetNamebyref(string refconnection, SafetyTemplate st)
        {
            string name = "";
            contact mycontact = Getcontact(refconnection, st);
            if (mycontact != null)

                name = mycontact.Name;

            if (name == "")
            {
                if (GetBlokName(refconnection, st) != null)
                    name = GetBlokName(refconnection, st).localId;
                else
                    name = "";
            }
            return name;
        }

        private static SafetyBlock GetBlokName(string refconnection, SafetyTemplate st)
        {
            //string blockname = "";
            SafetyBlock block = (from S in st.SafetyBlockMap.Values
                                 where S.localId == refconnection
                                 select S).FirstOrDefault();
            //if (block != null)
            //{
            //    //if (block.instanceName != "" || block.instanceName != null)
            //    //    blockname = block.instanceName;
            //    //else
            //    //    blockname = block.typeName;
            //    blockname = block.localId;
            //}
            return block;
        }

        private static contact Getcontact(string refconnection, SafetyTemplate st)
        {
            string coilname = "";
            contact coil = (from S in st.SafetycontactMap.Values
                            where S.localId == refconnection
                            select S).FirstOrDefault();
            if (coil != null)
            {
                coilname = coil.Name;
            }
            return coil;
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

        public static contactconnectionPointIn DeserializeconnectionPointIn(XElement thispconnectionPointIn)
        {
            contactconnectionPointIn newinppin = new contactconnectionPointIn();
            var relPositionlist = from k in thispconnectionPointIn.Descendants()
                                  where k.Name.LocalName == "relPosition"
                                  select k;
            foreach (XElement thisrelPosition in relPositionlist)
            {
                relPosition newrelPosition = new relPosition();
                newrelPosition.X = thisrelPosition.Attribute("x").Value;
                newrelPosition.Y = thisrelPosition.Attribute("y").Value;
                newrelPosition.MyXML = thisrelPosition;
                newinppin.relPositionMap.Add(newrelPosition);
                var connectionlist = (from L in thispconnectionPointIn.Descendants()
                                      where L.Name.LocalName == "connection"
                                      select L).ToList();
                if (connectionlist.Count > 0)
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
                            newposition.MyXML = thisposition;
                            newconnection.positionMap.Add(newposition);
                        }
                        newinppin.connectionMap.Add(newconnection);
                    }
            }

            return newinppin;
        }
    }
}