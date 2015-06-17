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
 * Portions Copyrighted 2014-2015 ForgeRock AS.
 */
using System;

using Org.IdentityConnectors.Common;
using Org.IdentityConnectors.Common.Proxy;
using Org.IdentityConnectors.Common.Script;
using Org.IdentityConnectors.Common.Security;
using Org.IdentityConnectors.Framework.Api.Operations;
using Org.IdentityConnectors.Framework.Common.Objects;
using Org.IdentityConnectors.Framework.Common.Objects.Filters;
using Org.IdentityConnectors.Framework.Common.Exceptions;
using Org.IdentityConnectors.Framework.Common.Serializer;
using Org.IdentityConnectors.Framework.Spi;
using Org.IdentityConnectors.Framework.Spi.Operations;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Org.IdentityConnectors.Framework.Api;
using System.Text;
using System.Diagnostics;
using System.Threading;

namespace Org.IdentityConnectors.Framework.Impl.Api.Local.Operations
{
    #region APIOperationRunner
    /// <summary>
    /// NOTE: internal class, public only for unit tests
    /// Base class for API operation runners.
    /// </summary>
    public abstract class APIOperationRunner
    {
        /// <summary>
        /// Context that has all the information required to execute an operation.
        /// </summary>
        private readonly OperationalContext _context;

        /// <summary>
        /// Creates the API operation so it can called multiple times.
        /// </summary>
        public APIOperationRunner(OperationalContext context)
        {
            _context = context;
            //TODO: verify this
            // get the APIOperation that this class implements..
            //List<Class<? extends APIOperation>> apiOps = getInterfaces(this
            //.getClass(), APIOperation.class);
            // there should be only one..
            //if (apiOps.size() > 1) {
            //    final String MSG = "Must only implement one operation.";
            //    throw new IllegalStateException(MSG);
            //}
        }

        /// <summary>
        /// Get the current operational context.
        /// </summary>
        public OperationalContext GetOperationalContext()
        {
            return _context;
        }

    }
    #endregion

    #region ConnectorAPIOperationRunner
    /// <summary>
    /// NOTE: internal class, public only for unit tests
    /// Subclass of APIOperationRunner for operations that require a connector.
    /// </summary>
    public abstract class ConnectorAPIOperationRunner : APIOperationRunner
    {
        /// <summary>
        /// The connector instance
        /// </summary>
        private readonly Connector _connector;

        /// <summary>
        /// Creates the API operation so it can called multiple times.
        /// </summary>
        public ConnectorAPIOperationRunner(ConnectorOperationalContext context,
                                           Connector connector)
            : base(context)
        {
            _connector = connector;
        }

        public Connector GetConnector()
        {
            return _connector;
        }

        public ObjectNormalizerFacade GetNormalizer(ObjectClass oclass)
        {
            AttributeNormalizer norm = null;
            Connector connector = GetConnector();
            if (connector is AttributeNormalizer)
            {
                norm = (AttributeNormalizer)connector;
            }
            return new ObjectNormalizerFacade(oclass, norm);
        }
    }
    #endregion

    #region ConnectorAPIOperationRunnerProxy
    /// <summary>
    /// Proxy for APIOperationRunner that takes care of setting up underlying
    /// connector and creating the implementation of APIOperationRunner.
    /// </summary>
    /// <remarks>
    /// The implementation of APIOperationRunner gets created whenever the
    /// actual method is invoked.
    /// </remarks>
    internal class ConnectorAPIOperationRunnerProxy : InvocationHandler
    {
        /// <summary>
        /// The operational context
        /// </summary>
        private readonly ConnectorOperationalContext _context;

        /// <summary>
        /// The implementation constructor.
        /// </summary>
        /// <remarks>
        /// The instance is lazily created upon
        /// invocation
        /// </remarks>
        private readonly ConstructorInfo _runnerImplConstructor;

        private readonly Func<ConnectorOperationalContext, Connector, APIOperationRunner> _runnerImplFunc;

        /// <summary>
        /// Create an APIOperationRunnerProxy
        /// </summary>
        /// <param name="context">The operational context</param>
        /// <param name="runnerImplConstructor">The implementation constructor. Implementation
        /// must define a two-argument constructor(OperationalContext,Connector)</param>
        public ConnectorAPIOperationRunnerProxy(ConnectorOperationalContext context,
                ConstructorInfo runnerImplConstructor)
        {
            _context = context;
            _runnerImplConstructor = runnerImplConstructor;
        }

        public ConnectorAPIOperationRunnerProxy(ConnectorOperationalContext context,
                Func<ConnectorOperationalContext, Connector, APIOperationRunner> runnerImplConstructor)
        {
            _context = context;
            _runnerImplFunc = runnerImplConstructor;
        }
        
        public Object Invoke(Object proxy, MethodInfo method, object[] args)
        {
            //do not proxy equals, hashCode, toString
            if (method.DeclaringType.Equals(typeof(object)))
            {
                return method.Invoke(this, args);
            }
            object ret = null;
            Connector connector = null;
            ObjectPool<PoolableConnector> pool = _context.Pool;
            // get the connector class..
            SafeType<Connector> connectorClazz = _context.GetConnectorClass();
            try
            {
                // pooling is implemented get one..
                if (pool != null)
                {
                    connector = pool.BorrowObject();
                }
                else
                {
                    // get a new instance of the connector..
                    connector = connectorClazz.CreateInstance();
                    // initialize the connector..
                    connector.Init(_context.GetConfiguration());
                }
                APIOperationRunner runner = null != _runnerImplFunc ? _runnerImplFunc(_context, connector) :
                    (APIOperationRunner)_runnerImplConstructor.Invoke(new object[]{
                                                      _context,
                                                      connector});
                ret = method.Invoke(runner, args);
                // call out to the operation..
                if (ret is ISubscription)
                {
                    //Dispose later
                    ret = new DeferredSubscriptionDisposer((ISubscription)ret, connector, pool);
                    connector = null;
                }
            }
            catch (TargetInvocationException e)
            {
                Exception root = e.InnerException;
                ExceptionUtil.PreserveStackTrace(root);
                throw root;
            }
            finally
            {
                // make sure dispose of the connector properly
                if (connector != null)
                {
                    DisposeConnector(connector, pool);
                }
            }
            return ret;
        }

        internal static void DisposeConnector(Connector connector, ObjectPool<PoolableConnector> pool)
        {
            // determine if there was a pool..
            if (pool != null)
            {
                try
                {
                    //try to return it to the pool even though an
                    //exception may have happened that leaves it in
                    //a bad state. The contract of checkAlive
                    //is that it will tell you if the connector is
                    //still valid and so we leave it up to the pool
                    //and connector to work it out.
                    pool.ReturnObject((PoolableConnector)connector);
                }
                catch (Exception e)
                {
                    //don't let pool exceptions propagate or mask
                    //other exceptions. do log it though.
                    TraceUtil.TraceException(null, e);
                }
            }
            //not pooled - just dispose
            else
            {
                //dispose it not supposed to throw, but just in case,
                //catch the exception and log it so we know about it
                //but don't let the exception prevent additional
                //cleanup that needs to happen
                try
                {
                    connector.Dispose();
                }
                catch (Exception e)
                {
                    //log this though
                    TraceUtil.TraceException(null, e);
                }
            }
        }
        private sealed class DeferredSubscriptionDisposer : ISubscription
        {
            private readonly Connector _connector;
            private readonly ObjectPool<PoolableConnector> _poolEntry;
            private readonly ISubscription _subscription;
            private Int32 _active = 1;
            public DeferredSubscriptionDisposer(ISubscription subscription, Connector connector, ObjectPool<PoolableConnector> poolEntry)
            {
                _subscription = subscription;
                _connector = connector;
                _poolEntry = poolEntry;
            }

            public void Dispose()
            {
                try
                {
                    _subscription.Dispose();
                }
                finally
                {
                    if (Interlocked.CompareExchange(ref _active, 0, 1) == 1)
                    {
                        DisposeConnector(_connector, _poolEntry);
                    }
                }
            }

            public bool Unsubscribed
            {
                get
                {
                    return _subscription.Unsubscribed;
                }
            }
        }
    }

    #endregion

    #region ConnectorOperationalContext
    /// <summary>
    /// NOTE: internal class, public only for unit tests
    /// Simple structure to pass more variables through the constructor of
    /// <see cref="APIOperationRunner" />.
    /// </summary>
    public class ConnectorOperationalContext : OperationalContext
    {
        /// <summary>
        /// Pool Key for Connectors
        /// </summary>
        private ConnectorPoolManager.ConnectorPoolKey connectorPoolKey;

        public ConnectorOperationalContext(LocalConnectorInfoImpl connectorInfo, APIConfigurationImpl apiConfiguration)
            : base(connectorInfo, apiConfiguration)
        {
        }

        public ObjectPool<PoolableConnector> Pool
        {
            get
            {
                if (apiConfiguration.IsConnectorPoolingSupported)
                {
                    if (null == connectorPoolKey)
                    {
                        Pair<ConnectorPoolManager.ConnectorPoolKey, ObjectPool<PoolableConnector>> pool = ConnectorPoolManager.GetPool(apiConfiguration, connectorInfo);

                        connectorPoolKey = pool.First;
                        return pool.Second;
                    }
                    else
                    {
                        ObjectPool<PoolableConnector> pool = ConnectorPoolManager.GetPool(connectorPoolKey);
                        if (null == pool)
                        {
                            //
                            Pair<ConnectorPoolManager.ConnectorPoolKey, ObjectPool<PoolableConnector>> poolPair = ConnectorPoolManager.GetPool(apiConfiguration, connectorInfo);

                            connectorPoolKey = poolPair.First;
                            pool = poolPair.Second;
                        }
                        return pool;
                    }
                }
                else
                {
                    return null;
                }
            }
        }

        public SafeType<Connector> GetConnectorClass()
        {
            return GetConnectorInfo().ConnectorClass;
        }

        public override void Dispose()
        {
            base.Dispose();
            if (null != connectorPoolKey)
            {
                ConnectorPoolManager.Dispose(connectorPoolKey);
                connectorPoolKey = null;
            }
        }

    }
    #endregion

    #region AuthenticationImpl
    internal class AuthenticationImpl : ConnectorAPIOperationRunner,
            AuthenticationApiOp
    {
        /// <summary>
        /// Pass the configuration etc to the abstract class.
        /// </summary>
        public AuthenticationImpl(ConnectorOperationalContext context,
                Connector connector)
            : base(context, connector)
        {
        }

        /// <summary>
        /// Authenticate using the basic credentials.
        /// </summary>
        /// <seealso cref="Org.IdentityConnectors.Framework.Api.Operations.AuthenticationApiOp.Authenticate(String, String)" />
        public Uid Authenticate(ObjectClass objectClass, String username, GuardedString password, OperationOptions options)
        {
            Assertions.NullCheck(objectClass, "objectClass");
            if (ObjectClass.ALL.Equals(objectClass))
            {
                throw new System.NotSupportedException("Operation is not allowed on __ALL__ object class");
            }
            Assertions.NullCheck(username, "username");
            Assertions.NullCheck(password, "password");
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }
            return ((AuthenticateOp)GetConnector()).Authenticate(objectClass, username, password, options);
        }
    }
    #endregion

    #region ResolveUsernameImpl
    internal class ResolveUsernameImpl : ConnectorAPIOperationRunner,
            ResolveUsernameApiOp
    {
        /// <summary>
        /// Pass the configuration etc to the abstract class.
        /// </summary>
        public ResolveUsernameImpl(ConnectorOperationalContext context,
                Connector connector)
            : base(context, connector)
        {
        }

        /// <summary>
        /// Resolve the username to an <code>Uid</code>.
        /// </summary>
        public Uid ResolveUsername(ObjectClass objectClass, String username, OperationOptions options)
        {
            Assertions.NullCheck(objectClass, "objectClass");
            if (ObjectClass.ALL.Equals(objectClass))
            {
                throw new System.NotSupportedException("Operation is not allowed on __ALL__ object class");
            }
            Assertions.NullCheck(username, "username");
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }
            return ((ResolveUsernameOp)GetConnector()).ResolveUsername(objectClass, username, options);
        }
    }
    #endregion

    #region CreateImpl
    internal class CreateImpl : ConnectorAPIOperationRunner,
            CreateApiOp
    {
        /// <summary>
        /// Initializes the operation works.
        /// </summary>
        public CreateImpl(ConnectorOperationalContext context,
                Connector connector)
            : base(context, connector)
        {
        }

        /// <summary>
        /// Calls the create method on the Connector side.
        /// </summary>
        /// <seealso cref="Org.IdentityConnectors.Framework.Api.Operations.CreateApiOp.Create" />
        public Uid Create(ObjectClass objectClass, ICollection<ConnectorAttribute> createAttributes, OperationOptions options)
        {
            Assertions.NullCheck(objectClass, "objectClass");
            if (ObjectClass.ALL.Equals(objectClass))
            {
                throw new System.NotSupportedException("Operation is not allowed on __ALL__ object class");
            }
            Assertions.NullCheck(createAttributes, "createAttributes");
            // check to make sure there's not a uid..
            if (ConnectorAttributeUtil.GetUidAttribute(createAttributes) != null)
            {
                throw new InvalidAttributeValueException("Parameter 'createAttributes' contains a uid.");
            }
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }
            // validate input..
            HashSet<string> dups = new HashSet<string>();
            foreach (ConnectorAttribute attr in createAttributes)
            {
                if (dups.Contains(attr.Name))
                {
                    throw new InvalidAttributeValueException("Duplicate attribute name exists: " + attr.Name);
                }
                dups.Add(attr.Name);
            }
            Connector connector = GetConnector();
            ObjectNormalizerFacade normalizer = GetNormalizer(objectClass);
            ICollection<ConnectorAttribute> normalizedAttributes =
                normalizer.NormalizeAttributes(createAttributes);
            // create the object..
            Uid ret = ((CreateOp)connector).Create(objectClass, normalizedAttributes, options);
            return (Uid)normalizer.NormalizeAttribute(ret);
        }
    }
    #endregion

    #region DeleteImpl
    internal class DeleteImpl : ConnectorAPIOperationRunner,
            DeleteApiOp
    {
        /// <summary>
        /// Initializes the operation works.
        /// </summary>
        public DeleteImpl(ConnectorOperationalContext context,
                Connector connector)
            : base(context, connector)
        {
        }
        /// <summary>
        /// Calls the delete method on the Connector side.
        /// </summary>
        /// <seealso cref="Org.IdentityConnectors.Framework.Api.Operations.CreateApiOp" />
        public void Delete(ObjectClass objectClass, Uid uid, OperationOptions options)
        {
            Assertions.NullCheck(objectClass, "objectClass");
            if (ObjectClass.ALL.Equals(objectClass))
            {
                throw new System.NotSupportedException("Operation is not allowed on __ALL__ object class");
            }
            Assertions.NullCheck(uid, "uid");
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }
            Connector connector = GetConnector();
            ObjectNormalizerFacade normalizer = GetNormalizer(objectClass);
            // delete the object..
            ((DeleteOp)connector).Delete(objectClass,
                                          (Uid)normalizer.NormalizeAttribute(uid),
                                          options);
        }
    }
    #endregion

    #region AttributesToGetResultsHandler
    public abstract class AttributesToGetResultsHandler
    {
        // =======================================================================
        // Fields
        // =======================================================================
        private readonly string[] _attrsToGet;

        // =======================================================================
        // Constructors
        // =======================================================================
        /// <summary>
        /// Keep the attribute to get..
        /// </summary>
        public AttributesToGetResultsHandler(string[] attrsToGet)
        {
            Assertions.NullCheck(attrsToGet, "attrsToGet");
            _attrsToGet = attrsToGet;
        }

        /// <summary>
        /// Simple method that clones the object and remove the attribute thats are
        /// not in the <see cref="OperationOptions.OP_ATTRIBUTES_TO_GET" /> set.
        /// </summary>
        /// <param name="attrsToGet">case insensitive set of attribute names.</param>
        public ICollection<ConnectorAttribute> ReduceToAttrsToGet(
            ICollection<ConnectorAttribute> attrs)
        {
            ICollection<ConnectorAttribute> ret = new HashSet<ConnectorAttribute>();
            IDictionary<string, ConnectorAttribute> map = ConnectorAttributeUtil.ToMap(attrs);
            foreach (string attrName in _attrsToGet)
            {
                ConnectorAttribute attr = CollectionUtil.GetValue(map, attrName, null);
                // TODO: Should we throw if the attribute is not yet it was
                // requested?? Or do we ignore because the API maybe asking
                // for what the resource doesn't have??
                if (attr != null)
                {
                    ret.Add(attr);
                }
            }
            return ret;
        }
        public ConnectorObject ReduceToAttrsToGet(ConnectorObject obj)
        {
            // clone the object and reduce the attributes only the set of
            // attributes.
            ConnectorObjectBuilder bld = new ConnectorObjectBuilder();
            bld.SetUid(obj.Uid);
            bld.SetName(obj.Name);
            bld.ObjectClass = obj.ObjectClass;
            ICollection<ConnectorAttribute> objAttrs = obj.GetAttributes();
            ICollection<ConnectorAttribute> attrs = ReduceToAttrsToGet(objAttrs);
            bld.AddAttributes(attrs);
            return bld.Build();
        }
    }
    #endregion

    #region SearchAttributesToGetResultsHandler
    public sealed class SearchAttributesToGetResultsHandler :
        AttributesToGetResultsHandler
    {
        // =======================================================================
        // Fields
        // =======================================================================
        private readonly ResultsHandler _handler;

        // =======================================================================
        // Constructors
        // =======================================================================
        public SearchAttributesToGetResultsHandler(
            ResultsHandler handler, string[] attrsToGet)
            : base(attrsToGet)
        {
            Assertions.NullCheck(handler, "handler");
            this._handler = handler;
        }

        public ResultsHandler ResultsHandler
        {
            get
            {
                return new ResultsHandler()
                {
                    Handle = obj =>
                    {
                        // clone the object and reduce the attributes only the set of
                        // attributes.
                        return _handler.Handle(ReduceToAttrsToGet(obj));
                    }
                };
            }
        }
    }
    #endregion

    #region SubscriptionImpl

    public class ConnectorEventSubscriptionApiOpImp : SubscriptionImpl, IConnectorEventSubscriptionApiOp
    {

        /// <summary>
        /// Creates the API operation so it can called multiple times.
        /// </summary>
        /// <param name="context"> </param>
        /// <param name="connector"> </param>
        /// <param name="referenceCounter"> </param>
        public ConnectorEventSubscriptionApiOpImp(ConnectorOperationalContext context, Connector connector, LocalConnectorFacadeImpl.ReferenceCounter referenceCounter)
            : base(context, connector, referenceCounter)
        {
        }

        public ISubscription Subscribe(ObjectClass objectClass, Filter eventFilter, IObserver<ConnectorObject> handler, OperationOptions operationOptions)
        {
            IConnectorEventSubscriptionOp operation = ((IConnectorEventSubscriptionOp)GetConnector());
            InternalObserver<ConnectorObject> observer = new InternalObserver<ConnectorObject>(handler, ReferenceCounter);
            try
            {
                ReferenceCounter.Acquire();
                return observer.Subscription(operation.Subscribe(objectClass, eventFilter, observer, operationOptions));
            }
            catch (Exception e)
            {
                observer.OnError(e);
                throw;
            }
        }
    }

    public class SyncEventSubscriptionApiOpImpl : SubscriptionImpl, ISyncEventSubscriptionApiOp
    {

        /// <summary>
        /// Creates the API operation so it can called multiple times.
        /// </summary>
        /// <param name="context"> </param>
        /// <param name="connector"> </param>
        /// <param name="referenceCounter"> </param>
        public SyncEventSubscriptionApiOpImpl(ConnectorOperationalContext context, Connector connector, LocalConnectorFacadeImpl.ReferenceCounter referenceCounter)
            : base(context, connector, referenceCounter)
        {
        }

        public ISubscription Subscribe(ObjectClass objectClass, SyncToken token, IObserver<SyncDelta> handler, OperationOptions operationOptions)
        {
            ISyncEventSubscriptionOp operation = ((ISyncEventSubscriptionOp)GetConnector());
            InternalObserver<SyncDelta> observer = new InternalObserver<SyncDelta>(handler, ReferenceCounter);
            try
            {
                ReferenceCounter.Acquire();
                return observer.Subscription(operation.Subscribe(objectClass, token, observer, operationOptions));
            }
            catch (Exception e)
            {
                observer.OnError(e);
                throw;
            }
        }
    }
    public class SubscriptionImpl : ConnectorAPIOperationRunner
    {

        protected readonly LocalConnectorFacadeImpl.ReferenceCounter ReferenceCounter;

        /// <summary>
        /// Creates the API operation so it can called multiple times.
        /// </summary>
        /// <param name="context"> </param>
        /// <param name="connector"> </param>
        protected internal SubscriptionImpl(ConnectorOperationalContext context, Connector connector, LocalConnectorFacadeImpl.ReferenceCounter referenceCounter)
            : base(context, connector)
        {
            ReferenceCounter = referenceCounter;
        }


        protected sealed class InternalObserver<T> : IObserver<T>
        {
            private readonly IObserver<T> _observer;
            private readonly CancellationSubscription _subscribed = new CancellationSubscription();

            public InternalObserver(IObserver<T> observer, LocalConnectorFacadeImpl.ReferenceCounter referenceCounter)
            {
                _observer = observer;
                _subscribed.Token.Register(referenceCounter.Release);
            }

            public CancellationSubscription Subscription(ISubscription subscription)
            {
                Assertions.NullCheck(subscription, "subscription");
                _subscribed.Token.Register(subscription.Dispose);
                return _subscribed;
            }

            public void OnCompleted()
            {
                if (!_subscribed.Unsubscribed)
                {
                    try
                    {
                        _subscribed.Dispose();
                    }
                    finally
                    {
                        _observer.OnCompleted();
                    }
                }
            }

            public void OnError(Exception e)
            {
                if (!_subscribed.Unsubscribed)
                {
                    try
                    {
                        _subscribed.Dispose();
                    }
                    finally
                    {
                        _observer.OnError(e);
                    }
                }
            }

            public void OnNext(T connectorObject)
            {
                try
                {
                    if (!_subscribed.Unsubscribed)
                    {
                        _observer.OnNext(connectorObject);
                    }
                }
                catch (Exception t)
                {
                    OnError(t);
                }
            }
        }
    }
    #endregion

    #region SyncAttributesToGetResultsHandler
    public sealed class SyncAttributesToGetResultsHandler :
        AttributesToGetResultsHandler
    {
        // =======================================================================
        // Fields
        // =======================================================================
        private readonly SyncResultsHandler _handler;

        // =======================================================================
        // Constructors
        // =======================================================================
        public SyncAttributesToGetResultsHandler(
            SyncResultsHandler handler, string[] attrsToGet)
            : base(attrsToGet)
        {
            Assertions.NullCheck(handler, "handler");
            this._handler = handler;
        }

        public SyncResultsHandler SyncResultsHandler
        {
            get
            {
                return new SyncResultsHandler()
                {
                    Handle = delta =>
                    {
                        SyncDeltaBuilder bld = new SyncDeltaBuilder();
                        bld.Uid = delta.Uid;
                        bld.Token = delta.Token;
                        bld.DeltaType = delta.DeltaType;
                        if (delta.Object != null)
                        {
                            bld.Object = ReduceToAttrsToGet(delta.Object);
                        }
                        return _handler.Handle(bld.Build());
                    }
                };
            }
        }
    }
    #endregion

    #region DuplicateFilteringResultsHandler
    public sealed class DuplicateFilteringResultsHandler
    {
        // =======================================================================
        // Fields
        // =======================================================================
        private readonly SearchResultsHandler _handler;
        private readonly HashSet<String> _visitedUIDs = new HashSet<String>();

        private bool _stillHandling = true;

        // =======================================================================
        // Constructors
        // =======================================================================
        /// <summary>
        /// Filter chain for producers.
        /// </summary>
        /// <param name="producer">Producer to filter.</param>
        public DuplicateFilteringResultsHandler(SearchResultsHandler handler)
        {
            // there must be a producer..
            if (handler == null)
            {
                throw new ArgumentException("Handler must not be null!");
            }
            this._handler = handler;
        }

        public SearchResultsHandler ResultsHandler
        {
            get
            {
                return new SearchResultsHandler()
                {
                    Handle = obj =>
                    {
                        String uid =
                    obj.Uid.GetUidValue();
                        if (!_visitedUIDs.Add(uid))
                        {
                            //we've already seen this - don't pass it
                            //throw
                            return true;
                        }
                        _stillHandling = _handler.Handle(obj);
                        return _stillHandling;
                    },
                    HandleResult = result =>
                    {
                        _handler.HandleResult(result);
                    }

                };
            }

        }

        public bool IsStillHandling
        {
            get
            {
                return _stillHandling;
            }
        }
    }
    #endregion

    #region FilteredResultsHandler
    public sealed class FilteredResultsHandler
    {
        // =======================================================================
        // Fields
        // =======================================================================
        readonly ResultsHandler handler;
        readonly Filter filter;

        // =======================================================================
        // Constructors
        // =======================================================================
        /// <summary>
        /// Filter chain for producers.
        /// </summary>
        /// <param name="producer">Producer to filter.</param>
        /// <param name="filter">Filter to use to accept objects.</param>
        public FilteredResultsHandler(ResultsHandler handler, Filter filter)
        {
            // there must be a producer..
            if (handler == null)
            {
                throw new ArgumentException("Producer must not be null!");
            }
            this.handler = handler;
            // use a default pass through filter..
            this.filter = filter == null ? new PassThroughFilter() : filter;
        }

        public ResultsHandler ResultsHandler
        {
            get
            {
                return new ResultsHandler
                {
                    Handle = obj =>
                    {
                        if (filter.Accept(obj))
                        {
                            return handler.Handle(obj);
                        }
                        else
                        {
                            return true;
                        }
                    }
                };
            }
        }

        /// <summary>
        /// Use a pass through filter to use if a null filter is provided.
        /// </summary>
        class PassThroughFilter : Filter
        {
            public bool Accept(ConnectorObject obj)
            {
                return true;
            }

            public R Accept<R, P>(FilterVisitor<R, P> v, P p)
            {
                return v.VisitExtendedFilter(p, this);
            }
        }
    }
    #endregion

    #region GetImpl
    /// <summary>
    /// Uses <see cref="Org.IdentityConnectors.Framework.Spi.Operations.SearchOp{T}" /> to find the object that is referenced by the
    /// <see cref="Uid" /> provided.
    /// </summary>
    public class GetImpl : GetApiOp
    {
        readonly SearchApiOp op;

        private class ResultAdapter
        {
            private IList<ConnectorObject> _list = new List<ConnectorObject>();
            public ResultsHandler ResultsHandler
            {
                get
                {
                    return new ResultsHandler()
                    {
                        Handle = obj =>
                        {
                            _list.Add(obj);
                            return false;
                        }
                    };
                }
            }
            public ConnectorObject GetResult()
            {
                return _list.Count == 0 ? null : _list[0];
            }
        }

        public GetImpl(SearchApiOp search)
        {
            this.op = search;
        }

        public ConnectorObject GetObject(ObjectClass objectClass, Uid uid, OperationOptions options)
        {
            Assertions.NullCheck(objectClass, "objectClass");
            if (ObjectClass.ALL.Equals(objectClass))
            {
                throw new System.NotSupportedException("Operation is not allowed on __ALL__ object class");
            }
            Assertions.NullCheck(uid, "uid");
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }
            Filter filter = FilterBuilder.EqualTo(uid);
            ResultAdapter adapter = new ResultAdapter();
            op.Search(objectClass, filter, adapter.ResultsHandler, options);
            return adapter.GetResult();
        }
    }
    #endregion

    #region OperationalContext
    /// <summary>
    /// NOTE: internal class, public only for unit tests
    /// OperationalContext - base class for operations that do not require a
    /// connector instance.
    /// </summary>
    public class OperationalContext
    {

        /// <summary>
        /// ConnectorInfo
        /// </summary>
        protected readonly LocalConnectorInfoImpl connectorInfo;

        /// <summary>
        /// Contains the <see cref="Org.IdentityConnectors.Framework.Spi.Connector" /> <see cref="Org.IdentityConnectors.Framework.Spi.Configuration" />.
        /// </summary>
        protected readonly APIConfigurationImpl apiConfiguration;

        private volatile Configuration configuration;

        /// <summary>
        /// Creates a new OperationalContext but it does not initiates the
        /// Configuration because the <seealso cref="#getConnectorInfo()"/> method must do it
        /// when it's called from a block where the classloader of the Thread is set
        /// to Connector.
        /// </summary>
        /// <param name="connectorInfo"> </param>
        /// <param name="apiConfiguration"> </param>
        public OperationalContext(LocalConnectorInfoImpl connectorInfo, APIConfigurationImpl apiConfiguration)
        {
            this.connectorInfo = connectorInfo;
            this.apiConfiguration = apiConfiguration;
        }

        /*
         * This method must be called when the Bundle ClassLoader is the Thread
         * Context ClassLoader.
         */
        public Configuration GetConfiguration()
        {

            if (null == configuration)
            {
                lock (this)
                {
                    if (null == configuration)
                    {
                        this.configuration = CSharpClassProperties.CreateBean((ConfigurationPropertiesImpl)this.apiConfiguration.ConfigurationProperties,
                connectorInfo.ConnectorConfigurationClass);
                    }
                }
            }
            return configuration;
        }

        protected LocalConnectorInfoImpl GetConnectorInfo()
        {

            return connectorInfo;

        }

        public ResultsHandlerConfiguration getResultsHandlerConfiguration()
        {
            return new ResultsHandlerConfiguration(apiConfiguration.ResultsHandlerConfiguration);
        }

        public virtual void Dispose()
        {
            if (configuration is StatefulConfiguration)
            {
                // dispose it not supposed to throw, but just in case,
                // catch the exception and log it so we know about it
                // but don't let the exception prevent additional
                // cleanup that needs to happen
                try
                {
                    StatefulConfiguration config = (StatefulConfiguration)configuration;
                    configuration = null;
                    config.Release();
                }
                catch (Exception e)
                {
                    // log this though
                    Trace.TraceWarning(e.Message);
                }
            }
        }
    }
    #endregion

    #region NormalizingResultsHandler
    public class NormalizingResultsHandler
    {
        private readonly ResultsHandler _target;
        private readonly ObjectNormalizerFacade _normalizer;

        public NormalizingResultsHandler(ResultsHandler target,
                ObjectNormalizerFacade normalizer)
        {
            Assertions.NullCheck(target, "target");
            Assertions.NullCheck(normalizer, "normalizer");
            _target = target;
            _normalizer = normalizer;
        }

        public ResultsHandler ResultsHandler
        {
            get
            {
                return new ResultsHandler()
                {

                    Handle = obj =>
                    {
                        ConnectorObject normalized = _normalizer.NormalizeObject(obj);
                        return _target.Handle(normalized);
                    }
                };
            }
        }
    }
    #endregion

    #region NormalizingSyncResultsHandler
    public class NormalizingSyncResultsHandler
    {
        private readonly SyncResultsHandler _target;
        private readonly ObjectNormalizerFacade _normalizer;

        public NormalizingSyncResultsHandler(SyncResultsHandler target,
                ObjectNormalizerFacade normalizer)
        {
            Assertions.NullCheck(target, "target");
            Assertions.NullCheck(normalizer, "normalizer");
            _target = target;
            _normalizer = normalizer;
        }

        public SyncResultsHandler SyncResultsHandler
        {
            get
            {
                return new SyncResultsHandler()
                {

                    Handle = delta =>
                    {
                        SyncDelta normalized = _normalizer.NormalizeSyncDelta(delta);
                        return _target.Handle(normalized);
                    }
                };
            }
        }
    }
    #endregion

    #region CaseNormalizer
    public sealed class CaseNormalizer : AttributeNormalizer
    {
        public static ObjectNormalizerFacade CreateCaseNormalizerFacade(ObjectClass oclass)
        {
            return new ObjectNormalizerFacade(oclass, new CaseNormalizer());
        }

        public ConnectorAttribute NormalizeAttribute(ObjectClass oclass, ConnectorAttribute attribute)
        {
            //            Trace.TraceInformation("Starting CaseNormalizer.NormalizeAttribute({0}, {1})", oclass, attribute.GetDetails());
            ConnectorAttribute rv = attribute;
            bool converted = false;

            IList<object> values = rv.Value;
            if (values != null)
            {
                IList<object> newValues = new List<object>();

                foreach (object value in values)
                {
                    if (value is string)
                    {
                        newValues.Add(((string)value).ToUpper());
                        converted = true;
                    }
                    else
                    {
                        newValues.Add(value);
                    }
                }

                if (converted)          // only when something changed; to save a few cpu cycles...
                {
                    rv = ConnectorAttributeBuilder.Build(attribute.Name, newValues);
                }
            }

            //            Trace.TraceInformation("Finishing CaseNormalizer.NormalizeAttribute, converted = {0}, return value = {1}", converted, rv.GetDetails());
            return rv;
        }
    }
    #endregion

    #region NormalizingFilter
    /// <summary>
    /// Proxy the filter to filter based on object normalized version.
    /// Similar to ObjectNormalizerFacade.NormalizeFilter,
    /// but this one DOES NOT expect that it gets object to be accepted/rejected
    /// in normalized form - it normalizes the object just before deciding.
    /// Currently used for case insensitive filtering.
    /// </summary>
    public sealed class NormalizingFilter : ExternallyChainedFilter
    {
        private readonly ObjectNormalizerFacade _normalizationFacade;

        public NormalizingFilter(Filter filter, ObjectNormalizerFacade facade)
            : base(facade.NormalizeFilter(filter))
        {
            _normalizationFacade = facade;
        }

        /// <summary>
        /// Return the decision based on normalized version of the object.
        /// </summary>
        /// <seealso cref="Filter.Accept(ConnectorObject)" />
        public override bool Accept(ConnectorObject obj)
        {
            bool result = Filter.Accept(_normalizationFacade.NormalizeObject(obj));
            //            Trace.TraceInformation("NormalizingFilter.Accept returns {0} for {1}", result, obj.GetAttributeByName("__NAME__"));
            return result;
        }

        public override R Accept<R, P>(FilterVisitor<R, P> v, P p)
        {
            return v.VisitExtendedFilter(p, this);
        }

        public override string ToString()
        {
            StringBuilder bld = new StringBuilder();
            bld.Append("NORMALIZE USING ").Append(_normalizationFacade).Append(": ").Append(Filter);
            return bld.ToString();
        }
    }
    #endregion

    #region ObjectNormalizerFacade
    public sealed class ObjectNormalizerFacade
    {
        /// <summary>
        /// The (non-null) object class
        /// </summary>
        private readonly ObjectClass _objectClass;
        /// <summary>
        /// The (possibly null) attribute normalizer
        /// </summary>
        private readonly AttributeNormalizer _normalizer;

        /// <summary>
        /// Create a new ObjectNormalizer
        /// </summary>
        /// <param name="objectClass">The object class</param>
        /// <param name="normalizer">The normalizer. May be null.</param>
        public ObjectNormalizerFacade(ObjectClass objectClass,
                AttributeNormalizer normalizer)
        {
            Assertions.NullCheck(objectClass, "objectClass");
            _objectClass = objectClass;
            _normalizer = normalizer;
        }

        /// <summary>
        /// Returns the normalized value of the attribute.
        /// </summary>
        /// <remarks>
        /// If no normalizer is specified, returns the original
        /// attribute.
        /// </remarks>
        /// <param name="attribute">The attribute to normalize.</param>
        /// <returns>The normalized attribute</returns>
        public ConnectorAttribute NormalizeAttribute(ConnectorAttribute attribute)
        {
            if (attribute == null)
            {
                return null;
            }
            else if (_normalizer != null)
            {
                return _normalizer.NormalizeAttribute(_objectClass, attribute);
            }
            else
            {
                return attribute;
            }
        }

        /// <summary>
        /// Returns the normalized set of attributes or null
        /// if the original set is null.
        /// </summary>
        /// <param name="attributes">The original attributes.</param>
        /// <returns>The normalized attributes or null if
        /// the original set is null.</returns>
        public ICollection<ConnectorAttribute> NormalizeAttributes(ICollection<ConnectorAttribute> attributes)
        {
            if (attributes == null)
            {
                return null;
            }
            ICollection<ConnectorAttribute> temp = new HashSet<ConnectorAttribute>();
            foreach (ConnectorAttribute attribute in attributes)
            {
                temp.Add(NormalizeAttribute(attribute));
            }
            return CollectionUtil.AsReadOnlySet(temp);
        }

        /// <summary>
        /// Returns the normalized object.
        /// </summary>
        /// <param name="orig">The original object</param>
        /// <returns>The normalized object.</returns>
        public ConnectorObject NormalizeObject(ConnectorObject orig)
        {
            return new ConnectorObject(orig.ObjectClass,
                                       NormalizeAttributes(orig.GetAttributes()));
        }

        /// <summary>
        /// Returns the normalized sync delta.
        /// </summary>
        /// <param name="delta">The original delta.</param>
        /// <returns>The normalized delta.</returns>
        public SyncDelta NormalizeSyncDelta(SyncDelta delta)
        {
            SyncDeltaBuilder builder = new
                SyncDeltaBuilder(delta);
            if (delta.Object != null)
            {
                builder.Object = NormalizeObject(delta.Object);
            }
            return builder.Build();
        }

        /// <summary>
        /// Returns a filter consisting of the original with
        /// all attributes normalized.
        /// </summary>
        /// <param name="filter">The original.</param>
        /// <returns>The normalized filter.</returns>
        public Filter NormalizeFilter(Filter filter)
        {
            if (filter is ContainsFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new ContainsFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is EndsWithFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new EndsWithFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is EqualsFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new EqualsFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is GreaterThanFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new GreaterThanFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is GreaterThanOrEqualFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new GreaterThanOrEqualFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is LessThanFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new LessThanFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is LessThanOrEqualFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new LessThanOrEqualFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is StartsWithFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new StartsWithFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is ContainsAllValuesFilter)
            {
                AttributeFilter afilter =
                    (AttributeFilter)filter;
                return new ContainsAllValuesFilter(NormalizeAttribute(afilter.GetAttribute()));
            }
            else if (filter is NotFilter)
            {
                NotFilter notFilter =
                    (NotFilter)filter;
                return new NotFilter(NormalizeFilter(notFilter.Filter));
            }
            else if (filter is AndFilter)
            {
                AndFilter andFilter =
                    (AndFilter)filter;
                return new AndFilter(NormalizeFilter(andFilter.Left),
                                     NormalizeFilter(andFilter.Right));
            }
            else if (filter is OrFilter)
            {
                OrFilter orFilter =
                    (OrFilter)filter;
                return new OrFilter(NormalizeFilter(orFilter.Left),
                                    NormalizeFilter(orFilter.Right));
            }
            else
            {
                return filter;
            }
        }
    }
    #endregion

    #region SchemaImpl
    internal class SchemaImpl : ConnectorAPIOperationRunner, SchemaApiOp
    {
        /// <summary>
        /// Initializes the operation works.
        /// </summary>
        public SchemaImpl(ConnectorOperationalContext context,
                Connector connector)
            : base(context, connector)
        {
        }

        /// <summary>
        /// Retrieve the schema from the <see cref="Org.IdentityConnectors.Framework.Spi.Connector" />.
        /// </summary>
        /// <seealso cref="Org.IdentityConnectors.Framework.Api.Operations.SchemaApiOp.Schema" />
        public Schema Schema()
        {
            return ((SchemaOp)GetConnector()).Schema();
        }
    }
    #endregion

    #region ScriptOnConnectorImpl
    public class ScriptOnConnectorImpl : ConnectorAPIOperationRunner,
            ScriptOnConnectorApiOp
    {
        public ScriptOnConnectorImpl(ConnectorOperationalContext context,
                                     Connector connector) :
            base(context, connector)
        {
        }

        public Object RunScriptOnConnector(ScriptContext request,
                OperationOptions options)
        {
            Assertions.NullCheck(request, "request");
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }
            Object rv;
            if (GetConnector() is ScriptOnConnectorOp)
            {
                rv = ((ScriptOnConnectorOp)GetConnector()).RunScriptOnConnector(request, options);
            }
            else
            {
                String language = request.ScriptLanguage;
                Assembly assembly = GetConnector().GetType().Assembly;

                ScriptExecutor executor =
                    ScriptExecutorFactory.NewInstance(language).NewScriptExecutor(
                            BuildReferenceList(assembly),
                            request.ScriptText,
                            false);
                IDictionary<String, Object> scriptArgs =
                    new Dictionary<String, Object>(request.ScriptArguments);
                scriptArgs["connector"] = GetConnector(); //add the connector instance itself
                rv = executor.Execute(scriptArgs);
            }
            return SerializerUtil.CloneObject(rv);
        }

        private Assembly[] BuildReferenceList(Assembly assembly)
        {
            List<Assembly> list = new List<Assembly>();
            foreach (var assemblyName in assembly.GetReferencedAssemblies())
            {
                list.Add(Assembly.Load(assemblyName));
            }
            // Just add the connector itself.
            list.Add(assembly);
            return list.ToArray();
        }
    }
    #endregion

    #region ScriptOnResourceImpl
    public class ScriptOnResourceImpl : ConnectorAPIOperationRunner,
            ScriptOnResourceApiOp
    {
        public ScriptOnResourceImpl(ConnectorOperationalContext context,
                                    Connector connector) :
            base(context, connector)
        {
        }

        public Object RunScriptOnResource(ScriptContext request,
                OperationOptions options)
        {
            Assertions.NullCheck(request, "request");
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }
            Object rv
               = ((ScriptOnResourceOp)GetConnector()).RunScriptOnResource(request, options);
            return SerializerUtil.CloneObject(rv);
        }

    }
    #endregion

    #region SearchImpl
    internal class SearchImpl : ConnectorAPIOperationRunner, SearchApiOp
    {
        /// <summary>
        /// Initializes the operation works.
        /// </summary>
        public SearchImpl(ConnectorOperationalContext context,
                Connector connector)
            : base(context, connector)
        {
        }

        /// <summary>
        /// Call the SPI search routines to return the results to the
        /// <see cref="ResultsHandler" />.
        /// </summary>
        /// <seealso cref="Org.IdentityConnectors.Framework.Api.Operations.SearchApiOp.Search(ObjectClass, Filter, ResultsHandler, OperationOptions)" />
        public SearchResult Search(ObjectClass objectClass, Filter originalFilter, ResultsHandler handler, OperationOptions options)
        {
            Assertions.NullCheck(objectClass, "objectClass");
            if (ObjectClass.ALL.Equals(objectClass))
            {
                throw new System.NotSupportedException("Operation is not allowed on __ALL__ object class");
            }
            Assertions.NullCheck(handler, "handler");
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }

            ResultsHandlerConfiguration hdlCfg = null != GetOperationalContext() ?
                                GetOperationalContext().getResultsHandlerConfiguration() : new ResultsHandlerConfiguration();
            ResultsHandler handlerChain = handler;
            Filter actualFilter = originalFilter;       // actualFilter is used for chaining filters - it points to the filter where new filters should be chained

            if (hdlCfg.EnableFilteredResultsHandler && hdlCfg.EnableCaseInsensitiveFilter && actualFilter != null)
            {
                Trace.TraceInformation("Creating case insensitive filter");
                ObjectNormalizerFacade normalizer = CaseNormalizer.CreateCaseNormalizerFacade(objectClass);
                actualFilter = new NormalizingFilter(actualFilter, normalizer);
            }

            if (hdlCfg.EnableNormalizingResultsHandler)
            {
                ObjectNormalizerFacade normalizer = GetNormalizer(objectClass);
                //chain a normalizing handler (must come before
                //filter handler)
                ResultsHandler normalizingHandler = new NormalizingResultsHandler(handler, normalizer).ResultsHandler;
                // chain a filter handler..
                if (hdlCfg.EnableFilteredResultsHandler)
                {
                    // chain a filter handler..
                    Filter normalizedFilter = normalizer.NormalizeFilter(actualFilter);
                    handlerChain = new FilteredResultsHandler(normalizingHandler, normalizedFilter).ResultsHandler;
                    actualFilter = normalizedFilter;
                }
                else
                {
                    handlerChain = normalizingHandler;
                }
            }
            else if (hdlCfg.EnableFilteredResultsHandler)
            {
                // chain a filter handler..
                handlerChain = new FilteredResultsHandler(handlerChain, actualFilter).ResultsHandler;
            }

            //get the IList interface that this type implements
            Type interfaceType = ReflectionUtil.FindInHierarchyOf
                (typeof(SearchOp<>), GetConnector().GetType());
            Type[] val = interfaceType.GetGenericArguments();
            if (val.Length != 1)
            {
                throw new Exception("Unexpected type: " + interfaceType);
            }
            Type queryType = val[0];
            Type searcherRawType = typeof(RawSearcherImpl<>);
            Type searcherType =
                searcherRawType.MakeGenericType(queryType);
            RawSearcher searcher = (RawSearcher)Activator.CreateInstance(searcherType);

            // add attributes to get handler
            string[] attrsToGet = options.AttributesToGet;
            if (attrsToGet != null && attrsToGet.Length > 0 && hdlCfg.EnableAttributesToGetSearchResultsHandler)
            {
                handlerChain = new SearchAttributesToGetResultsHandler(
                    handlerChain, attrsToGet).ResultsHandler;
            }
            SearchResult result = null;
            SearchResultsHandler innreHandler = new SearchResultsHandler()
            {
                Handle = obj =>
                {
                    return handlerChain.Handle(obj);
                },

                HandleResult = obj =>
                {
                    result = obj;
                }
            };
            searcher.RawSearch(GetConnector(), objectClass, actualFilter, innreHandler, options);
            return result;
        }
    }
    #endregion

    #region RawSearcher
    internal interface RawSearcher
    {
        /// <summary>
        /// Public because it is used by TestHelpers.
        /// </summary>
        /// <remarks>
        /// Raw,
        /// SPI-level search.
        /// </remarks>
        /// <param name="search">The underlying implementation of search
        /// (generally the connector itself)</param>
        /// <param name="oclass">The object class</param>
        /// <param name="filter">The filter</param>
        /// <param name="handler">The handler</param>
        /// <param name="options">The options</param>
        void RawSearch(Object search,
                ObjectClass oclass,
                Filter filter,
                SearchResultsHandler handler,
                OperationOptions options);
    }
    #endregion

    #region RawSearcherImpl
    internal class RawSearcherImpl<T> : RawSearcher where T : class
    {
        public void RawSearch(Object search,
                ObjectClass oclass,
                Filter filter,
                SearchResultsHandler handler,
                OperationOptions options)
        {
            RawSearch((SearchOp<T>)search, oclass, filter, handler, options);
        }

        /// <summary>
        /// Public because it is used by TestHelpers.
        /// </summary>
        /// <remarks>
        /// Raw,
        /// SPI-level search.
        /// </remarks>
        /// <param name="search">The underlying implementation of search
        /// (generally the connector itself)</param>
        /// <param name="oclass">The object class</param>
        /// <param name="filter">The filter</param>
        /// <param name="handler">The handler</param>
        /// <param name="options">The options</param>
        public static void RawSearch(SearchOp<T> search,
                ObjectClass oclass,
                Filter filter,
                SearchResultsHandler handler,
                OperationOptions options)
        {
            FilterTranslator<T> translator =
                search.CreateFilterTranslator(oclass, options);
            IList<T> queries =
                (IList<T>)translator.Translate(filter);
            if (queries.Count == 0)
            {
                search.ExecuteQuery(oclass,
                        null, handler, options);
            }
            else
            {
                //eliminate dups if more than one
                bool eliminateDups = queries.Count > 1;
                DuplicateFilteringResultsHandler dups = null;
                if (eliminateDups)
                {
                    dups = new DuplicateFilteringResultsHandler(handler);
                    handler = dups.ResultsHandler;
                }
                foreach (T query in queries)
                {
                    search.ExecuteQuery(oclass,
                            query, handler, options);
                    //don't run any more queries if the consumer
                    //has stopped
                    if (dups != null)
                    {
                        if (!dups.IsStillHandling)
                        {
                            break;
                        }
                    }
                }
            }
        }

    }
    #endregion

    #region SyncImpl
    public class SyncImpl : ConnectorAPIOperationRunner,
            SyncApiOp
    {
        public SyncImpl(ConnectorOperationalContext context,
                Connector connector)
            : base(context, connector)
        {
        }

        public SyncToken Sync(ObjectClass objectClass, SyncToken token, SyncResultsHandler handler, OperationOptions options)
        {
            //token is allowed to be null, objClass and handler must not be null
            Assertions.NullCheck(objectClass, "objectClass");
            Assertions.NullCheck(handler, "handler");
            //convert null into empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }
            // add a handler in the chain to remove attributes
            string[] attrsToGet = options.AttributesToGet;
            if (attrsToGet != null && attrsToGet.Length > 0)
            {
                handler = new SyncAttributesToGetResultsHandler(handler, attrsToGet).SyncResultsHandler;
            }
            //chain a normalizing results handler
            if (GetConnector() is AttributeNormalizer)
            {
                handler = new NormalizingSyncResultsHandler(handler, GetNormalizer(objectClass)).SyncResultsHandler;
            }

            SyncToken result = null;
            Boolean doAll = ObjectClass.ALL.Equals(objectClass);
            ((SyncOp)GetConnector()).Sync(objectClass, token, new SyncTokenResultsHandler()
            {
                Handle = delta =>
                {
                    if (doAll && SyncDeltaType.DELETE.Equals(delta.DeltaType)
                        && null == delta.ObjectClass)
                    {
                        throw new ConnectorException(
                                "Sync '__ALL__' operation requires the connector to set 'objectClass' parameter for sync event.");
                    }
                    return handler.Handle(delta);
                },
                HandleResult = obj =>
                {
                    result = obj;
                }
            }, options);
            return result;
        }

        public SyncToken GetLatestSyncToken(ObjectClass objectClass)
        {
            Assertions.NullCheck(objectClass, "objectClass");
            return ((SyncOp)GetConnector()).GetLatestSyncToken(objectClass);
        }
    }
    #endregion

    #region TestImpl
    /// <summary>
    /// Provides a method for the API to call the SPI's test method on the
    /// connector.
    /// </summary>
    /// <remarks>
    /// The test method is intended to determine if the <see cref="Org.IdentityConnectors.Framework.Spi.Connector" />
    /// is ready to perform the various operations it supports.
    /// </remarks>
    /// <author>Will Droste</author>
    internal class TestImpl : ConnectorAPIOperationRunner, TestApiOp
    {
        public TestImpl(ConnectorOperationalContext context, Connector connector)
            : base(context, connector)
        {
        }

        public void Test()
        {
            ((TestOp)GetConnector()).Test();
        }

    }
    #endregion

    #region UpdateImpl
    /// <summary>
    /// NOTE: internal class, public only for unit tests
    /// Handles both version of update this include simple replace and the advance
    /// update.
    /// </summary>
    public class UpdateImpl : ConnectorAPIOperationRunner, UpdateApiOp
    {
        /// <summary>
        /// All the operational attributes that can not be added or deleted.
        /// </summary>
        static readonly HashSet<String> OPERATIONAL_ATTRIBUTE_NAMES = new HashSet<String>();

        const String OPERATIONAL_ATTRIBUTE_ERR =
            "Operational attribute '{0}' can not be added or deleted only replaced.";

        static UpdateImpl()
        {
            OPERATIONAL_ATTRIBUTE_NAMES.Add(Name.NAME);
            CollectionUtil.AddAll(OPERATIONAL_ATTRIBUTE_NAMES,
                                  OperationalAttributes.OPERATIONAL_ATTRIBUTE_NAMES);
        }

        /// <summary>
        /// Determines which type of update a connector supports and then uses that
        /// handler.
        /// </summary>
        public UpdateImpl(ConnectorOperationalContext context,
                Connector connector)
            : base(context, connector)
        {
        }

        public Uid Update(ObjectClass objclass,
                Uid uid,
                ICollection<ConnectorAttribute> replaceAttributes,
                OperationOptions options)
        {
            // validate all the parameters..
            ValidateInput(objclass, uid, replaceAttributes, false);
            //cast null as empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }

            ObjectNormalizerFacade normalizer =
                GetNormalizer(objclass);
            uid = (Uid)normalizer.NormalizeAttribute(uid);
            replaceAttributes =
                normalizer.NormalizeAttributes(replaceAttributes);
            UpdateOp op = (UpdateOp)GetConnector();
            Uid ret = op.Update(objclass, uid, replaceAttributes, options);
            return (Uid)normalizer.NormalizeAttribute(ret);
        }

        public Uid AddAttributeValues(ObjectClass objectClass,
                Uid uid,
                ICollection<ConnectorAttribute> valuesToAdd,
                OperationOptions options)
        {
            // validate all the parameters..
            ValidateInput(objectClass, uid, valuesToAdd, true);
            //cast null as empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }

            ObjectNormalizerFacade normalizer =
                GetNormalizer(objectClass);
            uid = (Uid)normalizer.NormalizeAttribute(uid);
            valuesToAdd =
                normalizer.NormalizeAttributes(valuesToAdd);
            UpdateOp op = (UpdateOp)GetConnector();
            Uid ret;
            if (op is UpdateAttributeValuesOp)
            {
                UpdateAttributeValuesOp valueOp =
                    (UpdateAttributeValuesOp)op;
                ret = valueOp.AddAttributeValues(objectClass, uid, valuesToAdd, options);
            }
            else
            {
                ICollection<ConnectorAttribute> replaceAttributes =
                    FetchAndMerge(objectClass, uid, valuesToAdd, true, options);
                ret = op.Update(objectClass, uid, replaceAttributes, options);
            }
            return (Uid)normalizer.NormalizeAttribute(ret);
        }

        public Uid RemoveAttributeValues(ObjectClass objectClass,
                Uid uid,
                ICollection<ConnectorAttribute> valuesToRemove,
                OperationOptions options)
        {
            // validate all the parameters..
            ValidateInput(objectClass, uid, valuesToRemove, true);
            //cast null as empty
            if (options == null)
            {
                options = new OperationOptionsBuilder().Build();
            }

            ObjectNormalizerFacade normalizer =
                GetNormalizer(objectClass);
            uid = (Uid)normalizer.NormalizeAttribute(uid);
            valuesToRemove =
                normalizer.NormalizeAttributes(valuesToRemove);
            UpdateOp op = (UpdateOp)GetConnector();
            Uid ret;
            if (op is UpdateAttributeValuesOp)
            {
                UpdateAttributeValuesOp valueOp =
                    (UpdateAttributeValuesOp)op;
                ret = valueOp.RemoveAttributeValues(objectClass, uid, valuesToRemove, options);
            }
            else
            {
                ICollection<ConnectorAttribute> replaceAttributes =
                    FetchAndMerge(objectClass, uid, valuesToRemove, false, options);
                ret = op.Update(objectClass, uid, replaceAttributes, options);
            }
            return (Uid)normalizer.NormalizeAttribute(ret);
        }

        private ICollection<ConnectorAttribute> FetchAndMerge(ObjectClass objclass, Uid uid,
                ICollection<ConnectorAttribute> valuesToChange,
                bool add,
                OperationOptions options)
        {
            // check that this connector supports Search..
            if (ReflectionUtil.FindInHierarchyOf(typeof(SearchOp<>), GetConnector().GetType()) == null)
            {
                String MSG = "Connector must support search";
                throw new InvalidOperationException(MSG);
            }

            //add attrs to get to operation options, so that the
            //object we fetch has exactly the set of attributes we require
            //(there may be ones that are not in the default set)
            OperationOptionsBuilder builder = new OperationOptionsBuilder(options);
            ICollection<String> attrNames = new HashSet<String>();
            foreach (ConnectorAttribute attribute in valuesToChange)
            {
                attrNames.Add(attribute.Name);
            }
            builder.AttributesToGet = (attrNames.ToArray());
            options = builder.Build();

            // get the connector object from the resource...
            ConnectorObject o = GetConnectorObject(objclass, uid, options);
            if (o == null)
            {
                throw new UnknownUidException(uid, objclass);
            }
            // merge the update data..
            ICollection<ConnectorAttribute> mergeAttrs = Merge(valuesToChange, o.GetAttributes(), add);
            return mergeAttrs;
        }

        /// <summary>
        /// Merges two connector objects into a single updated object.
        /// </summary>
        public ICollection<ConnectorAttribute> Merge(ICollection<ConnectorAttribute> updateAttrs,
                ICollection<ConnectorAttribute> baseAttrs, bool add)
        {
            // return the merged attributes
            ICollection<ConnectorAttribute> ret = new HashSet<ConnectorAttribute>();
            // create map that can be modified to get the subset of changes 
            IDictionary<String, ConnectorAttribute> baseAttrMap = ConnectorAttributeUtil.ToMap(baseAttrs);
            // run through attributes of the current object..
            foreach (ConnectorAttribute updateAttr in updateAttrs)
            {
                // get the name of the update attributes
                String name = updateAttr.Name;
                // remove each attribute that is an update attribute..
                ConnectorAttribute baseAttr = CollectionUtil.GetValue(baseAttrMap, name, null);
                IList<Object> values;
                ConnectorAttribute modifiedAttr;
                if (add)
                {
                    if (baseAttr == null)
                    {
                        modifiedAttr = updateAttr;
                    }
                    else
                    {
                        // create a new list with the base attribute to add to..
                        values = CollectionUtil.NewList(baseAttr.Value);
                        CollectionUtil.AddAll(values, updateAttr.Value);
                        modifiedAttr = ConnectorAttributeBuilder.Build(name, values);
                    }
                }
                else
                {
                    if (baseAttr == null)
                    {
                        // nothing to actually do the attribute do not exist
                        continue;
                    }
                    else
                    {
                        // create a list with the base attribute to remove from..
                        values = CollectionUtil.NewList(baseAttr.Value);
                        foreach (Object val in updateAttr.Value)
                        {
                            values.Remove(val);
                        }
                        // if the values are empty send a null to the connector..
                        if (values.Count == 0)
                        {
                            modifiedAttr = ConnectorAttributeBuilder.Build(name);
                        }
                        else
                        {
                            modifiedAttr = ConnectorAttributeBuilder.Build(name, values);
                        }
                    }
                }
                ret.Add(modifiedAttr);
            }
            return ret;
        }

        /// <summary>
        /// Get the <see cref="ConnectorObject" /> to modify.
        /// </summary>
        private ConnectorObject GetConnectorObject(ObjectClass oclass, Uid uid, OperationOptions options)
        {
            // attempt to get the connector object..
            GetApiOp get = new GetImpl(new SearchImpl((ConnectorOperationalContext)GetOperationalContext(),
                    GetConnector()));
            return get.GetObject(oclass, uid, options);
        }

        /// <summary>
        /// Makes things easier if you can trust the input.
        /// </summary>
        public static void ValidateInput(ObjectClass objectClass,
                Uid uid,
                ICollection<ConnectorAttribute> replaceAttributes, bool isDelta)
        {
            Assertions.NullCheck(uid, "uid");
            Assertions.NullCheck(objectClass, "objectClass");
            if (ObjectClass.ALL.Equals(objectClass))
            {
                throw new System.NotSupportedException("Operation is not allowed on __ALL__ object class");
            }
            Assertions.NullCheck(replaceAttributes, "replaceAttributes");
            // check to make sure there's not a uid..
            if (ConnectorAttributeUtil.GetUidAttribute(replaceAttributes) != null)
            {
                throw new ArgumentException(
                        "Parameter 'attrs' contains a uid.");
            }
            // check for things only valid during ADD/DELETE
            if (isDelta)
            {
                foreach (ConnectorAttribute attr in replaceAttributes)
                {
                    Assertions.NullCheck(attr, "attr");
                    // make sure that none of the values are null..
                    if (attr.Value == null)
                    {
                        throw new ArgumentException(
                                "Can not add or remove a 'null' value.");
                    }
                    // make sure that if this an delete/add that it doesn't include
                    // certain attributes because it doesn't make any sense..
                    String name = attr.Name;
                    if (OPERATIONAL_ATTRIBUTE_NAMES.Contains(name))
                    {
                        String msg = String.Format(OPERATIONAL_ATTRIBUTE_ERR, name);
                        throw new ArgumentException(msg);
                    }
                }
            }
        }
    }
    #endregion

    #region ValidateImpl
    internal class ValidateImpl : APIOperationRunner, ValidateApiOp
    {

        public ValidateImpl(OperationalContext context)
            : base(context)
        {
        }

        public void Validate()
        {
            GetOperationalContext().GetConfiguration().Validate();
        }
    }
    #endregion
}