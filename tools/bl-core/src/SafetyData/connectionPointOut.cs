/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 * 
 * Purpose:             Data of ControlBlock.
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

namespace BL.SafetyData
{
    public class connectionPointOut
    {


        //public ParameterList ParameterList { get; set; }
        public List<relPosition> relPositionMap { get; set; }

        // public Dictionary<string, connection> connectionMap { get; set; }

        public connectionPointOut()
        {
            relPositionMap = new List<relPosition>();
            //ParameterList = new ParameterList();

            //connectionMap = new Dictionary<string, connection>();
        }

        public connectionPointOut(connectionPointOut copyFrom)
        {
            this.relPositionMap = new List<relPosition>(copyFrom.relPositionMap);// TODO
            //this.ParameterList = copyFrom.ParameterList == null ? null : new ParameterList(copyFrom.ParameterList);

            //this.connectionMap = new Dictionary<string, connection>(copyFrom.connectionMap);
        }
    }
}
