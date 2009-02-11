// <copyright file="ExchangeUtility.cs" company="Sun Microsystems, Inc.">
// ====================
// DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS HEADER.
// 
// Copyright 2007-2008 Sun Microsystems, Inc. All rights reserved.     
// 
// The contents of this file are subject to the terms of the Common Development 
// and Distribution License("CDDL") (the "License").  You may not use this file 
// except in compliance with the License.
// 
// You can obtain a copy of the License at 
// http://IdentityConnectors.dev.java.net/legal/license.txt
// See the License for the specific language governing permissions and limitations 
// under the License. 
// 
// When distributing the Covered Code, include this CDDL Header Notice in each file
// and include the License file at identityconnectors/legal/license.txt.
// If applicable, add the following below this CDDL Header, with the fields 
// enclosed by brackets [] replaced by your own identifying information: 
// "Portions Copyrighted [year] [name of copyright owner]"
// ====================
// </copyright>
// <author>Tomas Knappek</author>

namespace Org.IdentityConnectors.Exchange
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Management.Automation.Runspaces;
    using System.Reflection;
    using Microsoft.Win32;
    using Org.IdentityConnectors.ActiveDirectory;
    using Org.IdentityConnectors.Common;
    using Org.IdentityConnectors.Framework.Common.Objects;

    /// <summary>
    /// Description of ExchangeUtility.
    /// </summary>
    public sealed class ExchangeUtility : CommonUtils
    {
        /// <summary>
        /// class name, used for logging purposes
        /// </summary>
        private static readonly string ClassName = typeof(ExchangeUtility).ToString();

        /// <summary>
        /// Embedded xml resource file containg the object class definitions
        /// </summary>
        private const string FileObjectClassDef = "Org.IdentityConnectors.Exchange.ObjectClasses.xml";

        /// <summary>
        /// Exchange registry key, used for building the exchange assembly resolver
        /// </summary>
        private const string ExchangeRegKey = "Software\\Microsoft\\Exchange\\v8.0\\Setup\\";

        /// <summary>
        /// Exchange registry value name, used together with <see cref="ExchangeRegKey"/>
        /// </summary>
        private const string ExchangeRegValueName = "MsiInstallPath";

        /// <summary>
        /// Prevents a default instance of the <see cref="ExchangeUtility" /> class from being created. 
        /// </summary>
        private ExchangeUtility()
        {
        }

        /// <summary>
        /// Creates Exchange Assembly Resolver, <see cref="ResolveEventHandler"/>
        /// </summary>
        /// <param name="sender">The source of the event</param>
        /// <param name="args">A System.ResolveEventArgs that contains the event data</param>
        /// <returns>Assembly resolver that resolves Exchange assemblies</returns>
        internal static Assembly AssemblyResolver(object sender, ResolveEventArgs args)
        {
            // Add path for the Exchange 2007 DLLs
            if (args.Name.Contains("Microsoft.Exchange"))
            {
                string installPath = GetRegistryStringValue(ExchangeRegKey, ExchangeRegValueName);
                installPath += "\\bin\\" + args.Name.Split(',')[0] + ".dll";
                return Assembly.LoadFrom(installPath);
            }

            return null;
        }

        /// <summary>
        /// Get registry value, which is expected to be a string
        /// </summary>
        /// <param name="keyName">Registry Key Name</param>
        /// <param name="valName">Registry Value Name</param>
        /// <returns>Registry value</returns>        
        /// <exception cref="ArgumentNullException">If <paramref name="valName"/> is null</exception>
        /// <exception cref="InvalidDataException">If some problem with the registry value</exception>
        internal static string GetRegistryStringValue(string keyName, string valName)
        {
            const string MethodName = "GetRegistryStringValue";
            Debug.WriteLine(MethodName + "(" + keyName + ", " + valName + ")" + ":entry", ClassName);

            // argument check            
            if (keyName == null)
            {
                keyName = string.Empty;
            }

            if (valName == null)
            {
                throw new ArgumentNullException("valName");
            }

            RegistryKey regKey = Registry.LocalMachine.OpenSubKey(keyName, false);
            try
            {
                if (regKey != null)
                {
                    object val = regKey.GetValue(valName);
                    if (val != null)
                    {
                        RegistryValueKind regType = regKey.GetValueKind(valName);
                        if (!regType.Equals(RegistryValueKind.String))
                        {
                            throw new InvalidDataException(String.Format(
                                CultureInfo.CurrentCulture,
                                "Invalid Registry data type, key name: {0} value name: {1} should be String",
                                keyName,
                                valName));
                        }

                        return Convert.ToString(val, CultureInfo.CurrentCulture);
                    }
                    else
                    {
                        throw new InvalidDataException(String.Format(
                            CultureInfo.CurrentCulture,
                            "Missing value for key name: {0} value name: {1}",
                            keyName,
                            valName));
                    }
                }
                else
                {
                    throw new InvalidDataException(String.Format(
                        CultureInfo.CurrentCulture,
                        "Unable to open registry for key: {0}",
                        keyName));
                }
            }
            finally
            {
                if (regKey != null)
                {
                    regKey.Close();
                }

                Debug.WriteLine(MethodName + ":exit", ClassName);
            }
        }

        /// <summary>
        /// reads the object class info definitions from xml
        /// </summary>
        /// <returns>Dictionary of object classes</returns>
        internal static IDictionary<ObjectClass, ObjectClassInfo> GetOCInfo()
        {
            return GetOCInfo(FileObjectClassDef);
        }

        /// <summary>
        /// Creates command based on the commanf info, reading the calues from attributes
        /// </summary>
        /// <param name="cmdInfo">Command defition</param>
        /// <param name="attributes">Attribute values</param>
        /// <returns>Ready to execute Command</returns>             
        /// <exception cref="ArgumentNullException">if some of the param is null</exception>
        internal static Command GetCommand(ExchangeConnector.CommandInfo cmdInfo, ICollection<ConnectorAttribute> attributes)
        {
            Assertions.NullCheck(cmdInfo, "cmdInfo");
            Assertions.NullCheck(attributes, "attributes");

            // create command
            Command cmd = new Command(cmdInfo.Name);

            // map name attribute, if mapping specified
            if (!string.IsNullOrEmpty(cmdInfo.NameParameter))
            {                
                object val = GetAttValue(Name.NAME, attributes);
                if (val != null)
                {
                    cmd.Parameters.Add(cmdInfo.NameParameter, val);
                }
            }

            foreach (string attName in cmdInfo.Parameters)
            {
                object val = GetAttValue(attName, attributes);
                if (val != null)
                {
                    cmd.Parameters.Add(attName, val);
                }
            }

            return cmd;
        }

        /// <summary>
        /// Helper method: Gets attribute value from the attribute collection
        /// </summary>
        /// <param name="attName">attribute name</param>
        /// <param name="attributes">collection of attribute</param>
        /// <returns>Attribute value as object, null if not found</returns>     
        /// <exception cref="ArgumentNullException">If some of the params is null</exception>
        internal static object GetAttValue(string attName, ICollection<ConnectorAttribute> attributes)
        {
            Assertions.NullCheck(attName, "attName");
            Assertions.NullCheck(attributes, "attributes");

            object value = null;
            ConnectorAttribute attribute = ConnectorAttributeUtil.Find(attName, attributes);

            if (attribute != null)
            {
                value = ConnectorAttributeUtil.GetSingleValue(attribute);
            }

            return value;
        }

        /// <summary>
        /// Helper method for filtering the specified attributes from collection of attributes
        /// </summary>
        /// <param name="attributes">Collection of attributes</param>
        /// <param name="attName">Attribute names to be filtered out</param>
        /// <returns>Filtered collection of attributes</returns>
        internal static ICollection<ConnectorAttribute> FilterOut(ICollection<ConnectorAttribute> attributes, params string[] attName)
        {
            Assertions.NullCheck(attributes, "attributes");
            if (attName == null || attName.Length == 0)
            {
                return attributes;
            }

            IList names = new ArrayList(attName);            
            ICollection<ConnectorAttribute> filtered = new List<ConnectorAttribute>();
            foreach (ConnectorAttribute attribute in attributes)
            {
                if (!names.Contains(attribute.Name))
                {
                    filtered.Add(attribute);
                }
            }

            return filtered;
        }

        /// <summary>
        /// Helper method - Replaces specified collection Items
        /// TODO: reimplement not using arrays!
        /// </summary>        
        /// <param name="col">Input <see cref="ArrayList"/> to be searched for replacement</param>
        /// <param name="replace">Replace mappings</param>
        /// <returns>Replaced <see cref="ArrayList"/></returns>        
        /// <exception cref="ArgumentNullException">If some of the params is null</exception>
        internal static ArrayList FilterReplace(ArrayList col, string[,] replace)
        {
            Assertions.NullCheck(col, "col");
            Assertions.NullCheck(replace, "replace");

            ArrayList newcol = (ArrayList) col.Clone();
            for (int i = 0; i < replace.GetLength(0); i++)
            {
                if (newcol.Contains(replace[i, 0]))
                {                    
                    newcol.Remove(replace[i, 0]);
                    newcol.Add(replace[i, 1]);
                }
            }

            return newcol;
        }

        /// <summary>
        /// Finds the attributes in connector object and rename it according to input array of names, but only
        /// if the atribute name is in attributes to get
        /// </summary>
        /// <param name="cobject">ConnectorObject which attributes should be replaced</param>
        /// <param name="attsToGet">Attributes to get list</param>
        /// <param name="replace">Replace mapping</param>
        /// <returns>ConnectorObject with replaced attributes</returns>        
        /// <exception cref="ArgumentNullException">If some of the params is null</exception>
        internal static ConnectorObject ReplaceAttributes(ConnectorObject cobject, IList attsToGet, string[,] replace)
        {
            Assertions.NullCheck(cobject, "cobject");
            Assertions.NullCheck(attsToGet, "attsToGet");
            Assertions.NullCheck(replace, "replace");

            var attributes = cobject.GetAttributes();
            var builder = new ConnectorObjectBuilder();
            foreach (ConnectorAttribute attribute in attributes)
            {
                for (int i = 0; i < replace.GetLength(0); i++)
                {
                    string oldName = replace[i, 1];
                    string newName = replace[i, 0];
                    if (attsToGet.Contains(newName) && attribute.Name == oldName)
                    {
                        var newAttribute = RenameAttribute(attribute, replace[i, 0]);
                        builder.AddAttribute(newAttribute);
                        break;
                    }
                }

                builder.AddAttribute(attribute);
            }

            builder.AddAttributes(attributes);
            builder.ObjectClass = cobject.ObjectClass;
            builder.SetName(cobject.Name);
            builder.SetUid(cobject.Uid);
            return builder.Build();
        }

        /// <summary>
        /// Renames the connector attribute to new name
        /// </summary>
        /// <param name="cattribute">ConnectorAttribute to be renamed</param>
        /// <param name="newName">New attribute name</param>
        /// <returns>Renamed ConnectorAttribute</returns>
        /// <exception cref="ArgumentNullException">If some of the params is null</exception>
        internal static ConnectorAttribute RenameAttribute(ConnectorAttribute cattribute, string newName)
        {
            Assertions.NullCheck(cattribute, "cattribute");
            Assertions.NullCheck(newName, "newName");

            var attBuilder = new ConnectorAttributeBuilder();
            attBuilder.AddValue(cattribute.Value);
            attBuilder.Name = newName;
            return attBuilder.Build();
        }
    }
}