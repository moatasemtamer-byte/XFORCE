/********************************************************************************************
 * Copyright (c) Global Engineering Management by Invensys (refer to readme.txt for details)
 * 
 * Purpose:             Interface for templatesTree objects.
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

namespace BL
{
    public interface XMLTemplate
    {
        string GetName();
        object GetDataObject();
    }
}
