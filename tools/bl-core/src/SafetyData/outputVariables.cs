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
    public class outputVariables
    {
        public const int VISIBILITY_NONE = 0;
        public const int VISIBILITY_LEFT = 1;
        public const int VISIBILITY_RIGHT = 2;


        public Dictionary<string, variable> variableMap { get; set; }
        public int Visible { get; set; }

        public outputVariables()
        {
            Visible = VISIBILITY_RIGHT;
            variableMap = new Dictionary<string, variable>();
        }

        public outputVariables(outputVariables copyFrom)
        {
           // this.variableMap = new Dictionary<string, variable>(copyFrom.variableMap);
            this.variableMap = new Dictionary<string, variable>();

            foreach (KeyValuePair<string, variable> pair in copyFrom.variableMap)
            {
                variable newVar = new variable(pair.Value);
                this.variableMap.Add(pair.Key, newVar);

            }
            this.Visible = copyFrom.Visible;
        }
    }
}
