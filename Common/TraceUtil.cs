/*
 * ====================
 * DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS HEADER.
 * 
 * Copyright 2008-2009 Sun Microsystems, Inc. All rights reserved.     
 * 
 * The contents of this file are subject to the terms of the Common Development 
 * and Distribution License("CDDL") (the "License").  You may not use this file 
 * except in compliance with the License.
 * 
 * You can obtain a copy of the License at 
 * http://opensource.org/licenses/cddl1.php
 * See the License for the specific language governing permissions and limitations 
 * under the License. 
 * 
 * When distributing the Covered Code, include this CDDL Header Notice in each file
 * and include the License file at http://opensource.org/licenses/cddl1.php.
 * If applicable, add the following below this CDDL Header, with the fields 
 * enclosed by brackets [] replaced by your own identifying information: 
 * "Portions Copyrighted [year] [name of copyright owner]"
 * ====================
 */

using System;
using System.Diagnostics;
using System.Text;

namespace Org.IdentityConnectors.Common
{
    /// <summary>
    ///     Description of TraceUtil.
    /// </summary>
    public static class TraceUtil
    {
        /// <summary>
        /// Traces an exception with its stack trace
        /// </summary>
        /// <param name="level"></param>
        /// <param name="msg"></param>
        /// <param name="e"></param>
        /// <param name="arg"></param>
        /// <remarks>Since 1.5</remarks> 
        public static void TraceException(TraceLevel level, String msg, Exception e, params object[] arg)
        {
            StringBuilder builder = new StringBuilder();
            if (msg != null)
            {
                builder.AppendLine(String.Format(msg, arg));
            }
          
            if (e != null)
            {
                ExceptionToString(builder, e, string.Empty);
                
            }
            switch (level)
            {
                case TraceLevel.Off:
                    break;
                case TraceLevel.Verbose:
#if DEBUG
                    Debug.WriteLine(builder.ToString());
#else
                    Trace.TraceInformation(builder.ToString());
#endif
                    Trace.TraceInformation(builder.ToString());
                    break;
                case TraceLevel.Info:
                    Trace.TraceInformation(builder.ToString());
                    break;
                case TraceLevel.Warning:
                    Trace.TraceWarning(builder.ToString());
                    break;
                default:
                    Trace.TraceError(builder.ToString());
                    break;
            }
        }

        /// <summary>
        ///     Traces an exception with its stack trace
        /// </summary>
        /// <param name="msg">An optional error message to display in addition to the exception</param>
        /// <param name="e">The exception</param>
        public static void TraceException(String msg, Exception e)
        {
            StringBuilder builder = new StringBuilder();
            if (msg != null)
            {
                builder.AppendLine(msg);
            }
            if (e != null)
            {
                ExceptionToString(builder, e, string.Empty);
                //builder.AppendLine(e.ToString());
            }
            Trace.TraceError(builder.ToString());
        }

        public static void ExceptionToString(StringBuilder sb, Exception e, string indent)
        {
            if (indent == null)
            {
                indent = string.Empty;
            }
            else if (indent.Length > 0)
            {
                sb.AppendFormat("{0}Inner ", indent);
            }

            sb.AppendFormat("Exception :\n{0}Type: {1}", indent, e.GetType().FullName);
            sb.AppendFormat("\n{0}Message: {1}", indent, e.Message);
            sb.AppendFormat("\n{0}Source: {1}", indent, e.Source);
            sb.AppendFormat("\n{0}Stacktrace: {1}", indent, e.StackTrace);

            if (e.InnerException != null)
            {
                sb.Append("\n");
                ExceptionToString(sb, e.InnerException, indent + "  ");
            }
        }
    }
}