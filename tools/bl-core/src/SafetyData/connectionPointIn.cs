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
    public class connectionPointIn
    {


        //public ParameterList ParameterList { get; set; }
        public List<relPosition> relPositionMap { get; set; }

        public connection connectionMap { get; set; }

        public connectionPointIn()
        {
            relPositionMap = new List<relPosition>();
            //ParameterList = new ParameterList();

            connectionMap = new connection();
        }

        public connectionPointIn(connectionPointIn copyFrom)
        {
            this.relPositionMap = new List<relPosition>();// TODO
            foreach(var rel in copyFrom.relPositionMap)
            {
                this.relPositionMap.Add(new relPosition(rel));
            }
            //this.ParameterList = copyFrom.ParameterList == null ? null : new ParameterList(copyFrom.ParameterList);

            this.connectionMap = new connection(copyFrom.connectionMap);
        }
    }
}
