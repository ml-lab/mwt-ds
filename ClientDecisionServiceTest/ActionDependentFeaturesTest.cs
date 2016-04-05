﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Research.MultiWorldTesting.ExploreLibrary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Research.MultiWorldTesting.ClientLibrary;

namespace ClientDecisionServiceTest
{
    [TestClass]
    public class ActionDependentFeaturesTest
    {
        [TestMethod]
        public void TestADFExplorationResult()
        {
            joinServer.Reset();

            var dsConfig = new DecisionServiceConfiguration(MockCommandCenter.AuthorizationToken)
            {
                PollingForModelPeriod = TimeSpan.MinValue,
                PollingForSettingsPeriod = TimeSpan.MinValue,
                JoinServerType = JoinServerType.CustomSolution,
                LoggingServiceAddress = MockJoinServer.MockJoinServerAddress
            };

            var ranker = VWPolicy.StartWithRanker(dsConfig, new TestADFPolicy());
            using (var ds = DecisionServiceClient.Create(ranker.WithTopSlotEpsilonGreedy(epsilon: .5f)))
            {
                string uniqueKey = "eventid";

                for (int i = 1; i <= 100; i++)
                {
                    var adfContext = new TestADFContext(i);
                    uint[] action = ds.ChooseAction(new UniqueEventID { Key = uniqueKey }, adfContext, (uint)adfContext.ActionDependentFeatures.Count);

                    Assert.AreEqual(i, action.Length);

                    // verify all unique actions in the list
                    Assert.AreEqual(action.Length, action.Distinct().Count());

                    // verify the actions are in the expected range
                    Assert.AreEqual((i * (i + 1)) / 2, action.Sum(a => a));

                    ds.ReportReward(i / 100f, new UniqueEventID { Key = uniqueKey });
                }
            }
            Assert.AreEqual(200, joinServer.EventBatchList.Sum(b => b.ExperimentalUnitFragments.Count));
        }

        [TestMethod]
        public void TestADFModelUpdateFromFile()
        {
            joinServer.Reset();

            var dsConfig = new DecisionServiceConfiguration(MockCommandCenter.AuthorizationToken)
            {
                JoinServerType = JoinServerType.CustomSolution,
                LoggingServiceAddress = MockJoinServer.MockJoinServerAddress,
                PollingForModelPeriod = TimeSpan.MinValue,
                PollingForSettingsPeriod = TimeSpan.MinValue
            };

            var actualModelFiles = new List<string>();
            var ranker = VWPolicy.StartWithRanker(dsConfig, new TestADFWithFeaturesPolicy());
            using (var ds = DecisionServiceClient.Create(ranker.WithTopSlotEpsilonGreedy(epsilon: .5f)))
            {
                string uniqueKey = "eventid";

                string modelFile = "test_vw_adf{0}.model";

                for (int i = 1; i <= 100; i++)
                {
                    Random rg = new Random(i);

                    if (i % 50 == 1)
                    {
                        int modelIndex = i / 50;
                        string currentModelFile = string.Format(modelFile, modelIndex);

                        byte[] modelContent = commandCenter.GetCBADFModelBlobContent(numExamples: 3 + modelIndex, numFeatureVectors: 4 + modelIndex);
                        System.IO.File.WriteAllBytes(currentModelFile, modelContent);

                        ds.UpdateModel(File.OpenRead(currentModelFile));

                        actualModelFiles.Add(currentModelFile);
                    }

                    int numActions = rg.Next(5, 20);
                    var context = TestADFContextWithFeatures.CreateRandom(numActions, rg);

                    uint[] action = ds.ChooseAction(new UniqueEventID { Key = uniqueKey }, context, (uint)context.ActionDependentFeatures.Count);

                    Assert.AreEqual(numActions, action.Length);

                    // verify all unique actions in the list
                    Assert.AreEqual(action.Length, action.Distinct().Count());

                    // verify the actions are in the expected range
                    Assert.AreEqual((numActions * (numActions + 1)) / 2, action.Sum(a => a));

                    ds.ReportReward(i / 100f, new UniqueEventID { Key = uniqueKey });
                }
            }
            Assert.AreEqual(200, joinServer.EventBatchList.Sum(b => b.ExperimentalUnitFragments.Count));

            foreach (string actualModelFile in actualModelFiles)
            {
                System.IO.File.Delete(actualModelFile);
            }
        }

        [TestMethod]
        public void TestADFModelUpdateFromStream()
        {
            joinServer.Reset();

            var dsConfig = new DecisionServiceConfiguration(MockCommandCenter.AuthorizationToken)
            {
                JoinServerType = JoinServerType.CustomSolution,
                LoggingServiceAddress = MockJoinServer.MockJoinServerAddress,
                PollingForModelPeriod = TimeSpan.MinValue,
                PollingForSettingsPeriod = TimeSpan.MinValue
            };

            var ranker = VWPolicy.StartWithRanker(dsConfig, new TestADFWithFeaturesPolicy());
            using (var ds = DecisionServiceClient.Create(ranker.WithTopSlotEpsilonGreedy(epsilon: 0.5f)))
            {
                string uniqueKey = "eventid";

                string modelFile = "test_vw_adf{0}.model";

                for (int i = 1; i <= 100; i++)
                {
                    Random rg = new Random(i);

                    if (i % 50 == 1)
                    {
                        int modelIndex = i / 50;
                        string currentModelFile = string.Format(modelFile, modelIndex);

                        byte[] modelContent = commandCenter.GetCBADFModelBlobContent(numExamples: 3 + modelIndex, numFeatureVectors: 4 + modelIndex);

                        var modelStream = new MemoryStream(modelContent);

                        ds.UpdateModel(modelStream);
                    }

                    int numActions = rg.Next(5, 20);
                    var context = TestADFContextWithFeatures.CreateRandom(numActions, rg);

                    uint[] action = ds.ChooseAction(new UniqueEventID { Key = uniqueKey }, context, (uint)context.ActionDependentFeatures.Count);

                    Assert.AreEqual(numActions, action.Length);

                    // verify all unique actions in the list
                    Assert.AreEqual(action.Length, action.Distinct().Count());

                    // verify the actions are in the expected range
                    Assert.AreEqual((numActions * (numActions + 1)) / 2, action.Sum(a => a));

                    ds.ReportReward(i / 100f, new UniqueEventID { Key = uniqueKey });
                }

                ds.Flush();
            }
            Assert.AreEqual(200, joinServer.EventBatchList.Sum(b => b.ExperimentalUnitFragments.Count));
        }

        [TestInitialize]
        public void Setup()
        {
            joinServer = new MockJoinServer(MockJoinServer.MockJoinServerAddress);

            joinServer.Run();

            commandCenter = new MockCommandCenter(MockCommandCenter.AuthorizationToken);
        }

        [TestCleanup]
        public void CleanUp()
        {
            joinServer.Stop();
        }

        private static IReadOnlyCollection<TestADFFeatures> GetFeaturesFromContext(TestADFContextWithFeatures context)
        {
            return context.ActionDependentFeatures;
        }

        private MockJoinServer joinServer;
        private MockCommandCenter commandCenter;
    }
}