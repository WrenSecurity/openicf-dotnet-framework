/*
 * DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS HEADER.
 *
 * Copyright (c) 2013-2015 ForgeRock AS. All Rights Reserved
 *
 * The contents of this file are subject to the terms
 * of the Common Development and Distribution License
 * (the License). You may not use this file except in
 * compliance with the License.
 *
 * You can obtain a copy of the License at
 * http://forgerock.org/license/CDDLv1.0.html
 * See the License for the specific language governing
 * permission and limitations under the License.
 *
 * When distributing Covered Code, include this CDDL
 * Header Notice in each file and include the License file
 * at http://forgerock.org/license/CDDLv1.0.html
 * If applicable, add the following below the CDDL Header,
 * with the fields enclosed by brackets [] replaced by
 * your own identifying information:
 * "Portions Copyrighted [year] [name of copyright owner]"
 */

using System;
using NUnit.Framework;
using Org.IdentityConnectors.Framework.Api;
using Org.IdentityConnectors.Framework.Common;

namespace FrameworkTests
{
    [TestFixture]
    public class VersionRangeTests
    {
        [Test]
        public virtual void TestIsInRange()
        {
            Version reference0 = new Version(1, 1, 0, 0);
            Version reference1 = new Version(1, 1, 0, 1);
            Version reference2 = new Version(1, 1, 0, 2);
            Version reference3 = new Version(1, 1, 0, 3);
            Version reference4 = new Version(1, 1, 0, 4);
            VersionRange range = VersionRange.Parse("[1.1.0.1,1.1.0.3)");

            Assert.IsFalse(range.IsInRange(reference0));
            Assert.IsTrue(range.IsInRange(reference1));
            Assert.IsTrue(range.IsInRange(reference2));
            Assert.IsFalse(range.IsInRange(reference3));
            Assert.IsFalse(range.IsInRange(reference4));
        }

        [Test]
        public virtual void TestIsExact()
        {
            Assert.IsTrue(VersionRange.Parse("1.1.0.0").Exact);
            //Version string portion was too short or too long (major.minor[.build[.revision]]).
            //Assert.IsTrue(VersionRange.Parse("  [  1 , 1 ]  ").Exact); 
            Assert.IsTrue(VersionRange.Parse("[  1.1 , 1.1 ]").Exact);
            Assert.IsTrue(VersionRange.Parse("  [1.1.1 , 1.1.1]  ").Exact);
            Assert.IsTrue(VersionRange.Parse("[1.1.0.0,1.1.0.0]").Exact);
            Assert.IsTrue(VersionRange.Parse("(1.1.0.0,1.1.0.2)").Exact);
        }

        [Test]
        public virtual void TestIsEmpty()
        {
            Assert.IsTrue(VersionRange.Parse("(1.1.0.0,1.1.0.0)").Empty);
            Assert.IsTrue(VersionRange.Parse("(1.2.0.0,1.1.0.0]").Empty);
        }

        [Test]
        public virtual void TestValidSyntax()
        {
            try
            {
                VersionRange.Parse("(1.1.0.0)");
                Assert.Fail("Invalid syntax not failed");
            }
            catch (System.FormatException)
            {
                // ok
            }
            try
            {
                VersionRange.Parse("1.1.0.0,1.1)]");
                Assert.Fail("Invalid syntax not failed");
            }
            catch (System.ArgumentException)
            {
                // ok
            }
            try
            {
                VersionRange.Parse("(1.1.0.0-1.1)");
                Assert.Fail("Invalid syntax not failed");
            }
            catch (System.ArgumentException)
            {
                // ok
            }
            try
            {
                VersionRange.Parse("1.1.0.0,1.1");
                Assert.Fail("Invalid syntax not failed");
            }
            catch (System.ArgumentException)
            {
                // ok
            }
            try
            {
                VersionRange.Parse("( , 1.1)");
                Assert.Fail("Invalid syntax not failed");
            }
            catch (System.ArgumentException)
            {
                // ok
            }
        }

        [Test]
        public virtual void TestIsEqual()
        {
            VersionRange range1 = VersionRange.Parse("[1.1.0.1,1.1.0.3)");
            VersionRange range2 = VersionRange.Parse(range1.ToString());
            Assert.IsTrue(range1.Equals(range2));
        }

        [Test]
        public virtual void TestConnectorKeysInRange()
        {          
            ConnectorKeyRange r1 =
                ConnectorKeyRange.NewBuilder()
                    .SetBundleName("B")
                    .SetConnectorName("C")
                    .SetBundleVersion("1.1.0.0")
                    .Build();

            ConnectorKeyRange r2 =
                ConnectorKeyRange.NewBuilder()
                    .SetBundleName("B")
                    .SetConnectorName("C")
                    .SetBundleVersion("[1.1.0.0,1.2.0.0]")
                    .Build();

            ConnectorKey k1 = new ConnectorKey("B", "1.1.0.0", "C");
            ConnectorKey k2 = new ConnectorKey("B", "1.2.0.0", "C");

            Assert.IsTrue(r1.BundleVersionRange.Exact);
            Assert.IsFalse(r2.BundleVersionRange.Exact);

            Assert.IsTrue(r1.IsInRange(k1));
            Assert.IsFalse(r1.IsInRange(k2));

            Assert.IsTrue(r2.IsInRange(k1));
            Assert.IsTrue(r2.IsInRange(k2));

            ConnectorKeyRange r45 =
                ConnectorKeyRange.NewBuilder().SetBundleName("B").SetConnectorName("C")
                    .SetBundleVersion("[1.4.0.0,1.5.2.0)").Build();
            Assert.IsTrue(r45.IsInRange(new ConnectorKey("B", "1.4.0.0", "C")));

            Assert.IsFalse(r45.IsInRange(new ConnectorKey("B", "1.5.2.0", "C")));
        }
    }
}