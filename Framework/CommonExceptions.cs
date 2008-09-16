/*
 * Copyright 2008 Sun Microsystems, Inc. All rights reserved.
 * 
 * U.S. Government Rights - Commercial software. Government users 
 * are subject to the Sun Microsystems, Inc. standard license agreement
 * and applicable provisions of the FAR and its supplements.
 * 
 * Use is subject to license terms.
 * 
 * This distribution may include materials developed by third parties.
 * Sun, Sun Microsystems, the Sun logo, Java and Project Identity 
 * Connectors are trademarks or registered trademarks of Sun 
 * Microsystems, Inc. or its subsidiaries in the U.S. and other
 * countries.
 * 
 * UNIX is a registered trademark in the U.S. and other countries,
 * exclusively licensed through X/Open Company, Ltd. 
 * 
 * -----------
 * DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS HEADER.
 * 
 * Copyright 2008 Sun Microsystems, Inc. All rights reserved. 
 * 
 * The contents of this file are subject to the terms of the Common Development
 * and Distribution License(CDDL) (the License).  You may not use this file
 * except in  compliance with the License. 
 * 
 * You can obtain a copy of the License at
 * http://identityconnectors.dev.java.net/CDDLv1.0.html
 * See the License for the specific language governing permissions and 
 * limitations under the License.  
 * 
 * When distributing the Covered Code, include this CDDL Header Notice in each
 * file and include the License file at identityconnectors/legal/license.txt.
 * If applicable, add the following below this CDDL Header, with the fields 
 * enclosed by brackets [] replaced by your own identifying information: 
 * "Portions Copyrighted [year] [name of copyright owner]"
 * -----------
 */
using System;

namespace Org.IdentityConnectors.Framework.Common.Exceptions
{
   
    public class AlreadyExistsException : ConnectorException {
            
        public AlreadyExistsException() : base() {
        }
    
        public AlreadyExistsException(String message) : base(message)  {
            
        }
    
        public AlreadyExistsException(Exception ex) : base(ex) {
        }
    
        public AlreadyExistsException(String message, Exception ex) : base(message,ex) {
        }
    }
    
    public class ConfigurationException : ConnectorException {
            
        public ConfigurationException() : base() {
        }
    
        public ConfigurationException(String message) : base(message) {
        }
    
        public ConfigurationException(Exception ex) : base(ex) {
        }
    
        public ConfigurationException(String message, Exception ex) : base(message,ex) {
        }
    }
    
    public class ConnectionBrokenException : ConnectorIOException {
        
    
        public ConnectionBrokenException() : base() {
        }
        
        public ConnectionBrokenException(String msg) : base(msg) {
        }
        
        public ConnectionBrokenException(Exception ex) : base(ex) {
        }
    
        public ConnectionBrokenException(String message, Exception ex) : base(message,ex) {          
        }    
    } 
    public class ConnectionFailedException : ConnectorIOException {
        
    
        public ConnectionFailedException() : base() {
        }
        
        public ConnectionFailedException(String msg) : base(msg) {
        }
        
        public ConnectionFailedException(Exception ex) : base(ex) {
        }
    
        public ConnectionFailedException(String message, Exception ex) : base(message,ex) {
        }
    
    }
    
    public class ConnectorException : ApplicationException {
                
        public ConnectorException() : base() {
        }
    
        /**
         * Sets a message for the {@link Exception}.
         *  
         * @param message
         *            passed to the {@link RuntimeException} message.
         */
        public ConnectorException(String message) :base(message) {
        }
    
        /**
         * Sets the stack trace to the original exception, so this exception can
         * masquerade as the original only be a {@link RuntimeException}.
         * 
         * @param originalException
         *            the original exception adapted to {@link RuntimeException}.
         */
        public ConnectorException(Exception ex) : base(ex.Message,ex) {
        }
    
        /**
         * Sets the stack trace to the original exception, so this exception can
         * masquerade as the original only be a {@link RuntimeException}.
         * 
         * @param message
         * @param originalException
         *            the original exception adapted to {@link RuntimeException}.
         */
        public ConnectorException(String message, Exception originalException) : base(message,originalException) {
        }
    
    }
    
    public class ConnectorIOException : ConnectorException {
            
        public ConnectorIOException() : base() {
        }
        
        public ConnectorIOException(String msg) : base(msg) {
        }
        
        public ConnectorIOException(Exception ex) : base(ex) {
        }
    
        public ConnectorIOException(String message, Exception ex) : base(message,ex) {
        }
    }
    
    public class ConnectorSecurityException : ConnectorException {
            
        public ConnectorSecurityException() : base() {
        }
        
        public ConnectorSecurityException(String message) : base(message) {
        }
        
        public ConnectorSecurityException(Exception ex) : base(ex) {
        }
    
        public ConnectorSecurityException(String message, Exception ex) : base(message,ex) {
        }
    }
    
    public class InvalidCredentialException : ConnectorSecurityException {
        
        public InvalidCredentialException() : base() {
        }
        
        public InvalidCredentialException(String message) : base(message) {
        }
    
        public InvalidCredentialException(Exception ex) : base(ex) {
        }
    
        public InvalidCredentialException(String message, Exception ex) : base(message,ex) {
        }
    }
    public class InvalidPasswordException : InvalidCredentialException {
        
        public InvalidPasswordException() : base() {
        }
        
        public InvalidPasswordException(String message) : base(message) {
        }
    
        public InvalidPasswordException(Exception ex) : base(ex) {
        }
    
        public InvalidPasswordException(String message, Exception ex) : base(message,ex) {
        }
    }
    
    public class OperationTimeoutException : ConnectorException {
            
        public OperationTimeoutException() : base() {
        }
        
        public OperationTimeoutException(String msg) : base(msg) {
        }
        
        public OperationTimeoutException(Exception e) : base(e) {
        }
        
        public OperationTimeoutException(String msg, Exception e) : base(msg,e) {
        }
    
    }
    
    public class PermissionDeniedException : ConnectorSecurityException {
    
        public PermissionDeniedException() : base() {
        }
        
        public PermissionDeniedException(String message) : base(message) {
        }
    
        public PermissionDeniedException(Exception ex) : base(ex) {
        }
    
        public PermissionDeniedException(String message, Exception ex) : base(message,ex) {
        }
    }
    
    public class UnknownUidException : InvalidCredentialException {
        
        public UnknownUidException() : base() {
        }
    
        public UnknownUidException(String message) : base(message) {
        }
        
        public UnknownUidException(Exception ex) : base(ex) {
        }
    
        public UnknownUidException(String message, Exception ex) : base(message,ex) {
        }
    }

    
}