using System;
using System.IO;
using Smulator.SSD.Components;
using Smulator.Disk.SSD.NetworkGenerator;
using Smulator.BaseComponents;
using System.Xml;
using System.Xml.Serialization;
using System.Collections;
using Smulator.Util;
using Smulator.BaseComponents.Distributions;

namespace Smulator.Disk.SSD
{
    /// <summary>
    /// Summary description for Class1.
    /// </summary>
    /// 

    class SSD : MetaComponent, IParameterSetBasedExecutable
    {
        string runPath = @"E:\Simulation\runSSD1.xml";
        uint size;
        uint[] k;
        uint controllerNetworkAddress = uint.MaxValue;
        int seed = 2345;
        System.IO.TextWriter writer = null, summerizedResultFile = null, verySummerizedResultFile = null;
        XmlTextWriter xw;
        static string currentOutputFile = "";

        Controller SSDController = null;
        FlashChip[][] flashChips = null;
        bool writeToVerySummerizedFile = false;

        public SSD(string id, SSDParameterSet parameters)
            : base(id)
        {
            if (parameters != null)
                Build(parameters);
        }
        public string meshPres(uint[] k, uint val)
        {
            string resS = "";
            int[] res = new int[2];

            res[0] = (int)(val / k[1]);
            res[1] = (int)(val % k[1]);
            resS = "@" + res[0] + "@" + res[1];
            return resS;
        }

        public void Build(ExecutionParameterSet iparam)
        {
            SSDParameterSet parameters = iparam as SSDParameterSet;
            FlashChipGenerator fcg = new FlashChipGenerator();
            ControllerGenerator cg = new ControllerGenerator();
            XEngineFactory.XEngine.Reset(); // clear all components from engine

            if ((parameters.NetParameters.BusChannelCount == 0) || (parameters.NetParameters.ChipCountPerChannel == 0))
                throw new Exception("Parameter k should be greater than zero");

            //Increment network size to include disk controller
            size = (uint)(parameters.NetParameters.BusChannelCount * parameters.NetParameters.ChipCountPerChannel + 1);

            controllerNetworkAddress = (uint)size - 1;
            flashChips = new FlashChip[parameters.NetParameters.BusChannelCount][];
            for (int i = 0; i < parameters.NetParameters.BusChannelCount; i++)
            {
                flashChips[i] = new FlashChip[parameters.NetParameters.ChipCountPerChannel];
                for (int j = 0; j < parameters.NetParameters.ChipCountPerChannel; j++)
                    flashChips[i][j] = null;
            }

            MetaComponent SSD = this;
            SSD.Clear();

            k = new uint[2];
            k[0] = parameters.NetParameters.BusChannelCount; k[1] = parameters.NetParameters.ChipCountPerChannel;

            FTL.ChannelWidthInBit = NotListedNetworkParameters.channelWidth;
            FTL.ChannelWidthInByte = FTL.ChannelWidthInBit / FTL.ByteSize;

            uint maxSimultaneousReqsNum = 1;
            if (NotListedControllerParameterSet.MultiplaneCMDEnabled && NotListedControllerParameterSet.InterleavedCMDEnabled)
                maxSimultaneousReqsNum = parameters.FlashChipParameters.dieNoPerChip * parameters.FlashChipParameters.planeNoPerDie;
            else if (NotListedControllerParameterSet.MultiplaneCMDEnabled)
                maxSimultaneousReqsNum = parameters.FlashChipParameters.planeNoPerDie;
            else if (NotListedControllerParameterSet.InterleavedCMDEnabled)
                maxSimultaneousReqsNum = parameters.FlashChipParameters.dieNoPerChip;

            #region NodeGeneration
            for (uint i = 0; i < size - 1; i++)
            {
                flashChips[i / parameters.NetParameters.ChipCountPerChannel][i % parameters.NetParameters.ChipCountPerChannel] =
                     fcg.BuildNormalFlashChip(this.ID + ".Flashchip." + meshPres(k, i), (uint)(i / parameters.NetParameters.ChipCountPerChannel), (uint)(i % parameters.NetParameters.ChipCountPerChannel),
                     (uint)i, parameters.FlashChipParameters, NotListedNetworkParameters.readDataOutputReadyTime, ref flashChips[i / parameters.NetParameters.ChipCountPerChannel][i % parameters.NetParameters.ChipCountPerChannel]);
                SSD.AddXObject(flashChips[i / parameters.NetParameters.ChipCountPerChannel][i % parameters.NetParameters.ChipCountPerChannel]);
            }
            /* 
             *  ********
             *  *      *   chip00(0,0)   chip01(0,1)   chip02(0,2)   chip03(0,3)
             *  *      *     *             *             *             *
             *  *      *     *             *             *             *
             *  *      **************************************************00
             *  *      *
             *  *      *   chip04(1,0)   chip05(1,1)   chip06(1,2)   chip07(1,3)
             *  *      *     *             *             *             *
             *  *      *     *             *             *             *
             *  *      **************************************************01
             *  * ctrl *
             *  *  16  *
             *  *      *   chip08(2,0)   chip09(2,1)   chip10(2,2)   chip11(2,3)
             *  *      *     *             *             *             *
             *  *      *     *             *             *             *
             *  *      **************************************************02
             *  *      *
             *  *      *
             *  *      *   chip12(3,0)   chip13(3,1)   chip14(3,2)   chip15(3,3)
             *  *      *     *             *             *             *
             *  *      *     *             *             *             *
             *  *      **************************************************03
             *  ********
             */
            SSDController = cg.BuildControllerBus(this.ID + ".CTRL", parameters.ControllerParameters,
                parameters.FlashChipParameters, parameters.NetParameters, parameters.ControllerParameters.CopParameters, flashChips, NotListedExecutionParameterset.responseTimeLoggingFile, seed++);
            SSD.AddXObject(SSDController);
            #endregion

            SSDController.HostInterface.onStatReady += new HostInterface.StatReadyHandler(onStatisticsReady);
        }

        public void Simulate(ExecutionParameterSet iparam, ExecutionParameterSet iparamNext)
        {
            SSDParameterSet parameters = iparam as SSDParameterSet;
            SSDParameterSet nextParameters = iparamNext as SSDParameterSet;

            XEngineFactory.XEngine.Reset();
            parameters.Snapshot(Console.Out);

            MetaComponent SSD = this;

            /*string outputNames =
                (parameters.controllerParameters.WorkloadProperties.Type == HostInterface.RequestGeneratorType.TraceBased ?
                        parameters.controllerParameters.WorkloadProperties.filePath.Substring(0, parameters.controllerParameters.WorkloadProperties.filePath.LastIndexOf(".trace"))
                        + "." + (parameters.controllerParameters.WorkloadProperties.Mode == HostInterface.ReqeustGenerationMode.Normal ? "norm" : "sat")
                        :
                        parameters.controllerParameters.WorkloadProperties.filePath.Substring(0, parameters.controllerParameters.WorkloadProperties.filePath.LastIndexOf("\\"))
                        + "\\Synthetic." + (parameters.controllerParameters.WorkloadProperties.Mode == HostInterface.ReqeustGenerationMode.Normal ? "norm" : "sat")
                        + "(W=" + (100 - parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.ReadPercentage)
                        + "%.IntArr=" + ((double)parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.AverageRequestInterArrivalTime / 1000000).ToString("F4")
                        + "ms.Size=" + parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.reqSizeDistType
                        + "(" + (parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.reqSizeDistParam1 > 1000 ?
                        (parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.reqSizeDistParam1 / 1024).ToString("F0") :
                        ((double)parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.reqSizeDistParam1 / 1024).ToString("F1"))
                        + "K," + (parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.reqsizeDistParam2 / 1024)
                        + "K).Add=" + 
                        (parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.AddressDistType == HostInterfaceSynthetic.DistributionType.HotCold ?
                            "HC[f=" + ((double)parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.AddressDistParam1 / (double)100)
                            + ",r=" + ((double)parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.AddressDistParam2 / (double)100)
                            :
                            "U[" + ((double)parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.AddressDistParam1 / (double)largestLSN).ToString("P0")
                            + "," + ((double)parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.AddressDistParam2 / (double)largestLSN).ToString("P0")
                        )
                        + "]"
                )
                + "." + parameters.netParameters.chipsCommunication + parameters.netParameters.k1 + "x" + parameters.netParameters.k2
                + "." + "(d" + parameters.flashChipParameters.dieNoPerChip + "p" + parameters.flashChipParameters.planeNoPerDie + ")"
                + "." + "cw=" + parameters.netParameters.channelWidth
                + "." + "freq=" + (1000 / parameters.netParameters.readTransferCycleTime) + "MHz"
                + "." + "page=" + (parameters.flashChipParameters.pageCapacity / 1024) + "K"
                + (parameters.netParameters.chipsCommunication != DiskTopology.Bus ? ".inj=" + parameters.controllerParameters.injectionPolicy + ".pck=" + parameters.netParameters.packetSize + ".buf=" + parameters.netParameters.chipBufferDepth : "")
                + "." + "aloc=" + parameters.controllerParameters.planeAllocationScheme
                + "." + "gc=" + parameters.controllerParameters.GCProperties.GCPolicy
                + (parameters.controllerParameters.GCProperties.GCPolicy == GarbageCollector.GCPolicyType.RGA ? "(d=" + parameters.controllerParameters.GCProperties.RGAConstant + ")" : "")
                + (parameters.controllerParameters.GCProperties.GCPolicy == GarbageCollector.GCPolicyType.WindowedGreedy ? "(w=" + parameters.controllerParameters.GCProperties.WGreedyWindowSize + ")" : "")
                + (parameters.controllerParameters.MPREnabled ? ".MPR" : "")
                + (parameters.controllerParameters.MPWEnabled ? ".MPW" + (NotListedControllerParameterSet.useMPWGreedy ? "G" : "") : "");*/
            string outputNames = runPath;
            outputNames = outputNames.Remove(outputNames.LastIndexOf("."));
            NotListedExecutionParameterset.outputPath = outputNames + "-result.xml";
            NotListedExecutionParameterset.responseTimeLoggingFile = outputNames + ".log";
            NotListedExecutionParameterset.responseTimeAnalysisFile = outputNames + ".ana";

            if (writer == null)  // Start writing XML document 
            {
                writer = new System.IO.StreamWriter(NotListedExecutionParameterset.outputPath);
                currentOutputFile = NotListedExecutionParameterset.outputPath;
                xw = new XmlTextWriter(writer);
                xw.Formatting = System.Xml.Formatting.Indented;
                xw.WriteStartDocument();
                xw.WriteStartElement("Results");
            }

            xw.WriteStartElement("Result");
            //xw.WriteAttributeString("ID", parameters.id);

            Build(parameters);

            // Prompt user in console 
            Console.WriteLine("Time = " + DateTime.Now);
            DateTime startTime = DateTime.Now;
            Console.WriteLine("Simulating...");
            // Simulate
            XEngineFactory.XEngine.StartSimulation();
            DateTime endTime = DateTime.Now;
            Console.WriteLine("\n\nTime = " + DateTime.Now);
            Console.WriteLine("Duration = " + (endTime - startTime).ToString());
            Console.WriteLine("Smulator Time = " + XEngineFactory.XEngine.Time);

            // Write results
            xw.WriteAttributeString("Time", XEngineFactory.XEngine.Time.ToString());
            xw.WriteAttributeString("OutputFile", NotListedExecutionParameterset.outputPath);
            xw.WriteAttributeString("Duration", (endTime - startTime).ToString());


            xw.WriteStartElement("SSD");
            //Controller
            xw.WriteStartElement("Controller");
            SSDController.HostInterface.Snapshot("HostInterface", xw);
            SSDController.FTL.Snapshot("FTL", xw);
            SSDController.FTL.GarbageCollector.Snapshot("GarbageCollector", xw);
            xw.WriteEndElement();  //Controller

            for (uint i = 0; i < size; i++)
            {
                if (i == controllerNetworkAddress)
                    continue;
                ((FlashChip)SSD[SSD.ID + ".Flashchip." + meshPres(k, i)]).Snapshot("Flashchip", xw);
            }
            for (uint i = 0; i < size; i++)
            {
                if (i == controllerNetworkAddress)
                    continue;
                ((FlashChip)SSD[SSD.ID + ".Flashchip." + meshPres(k, i)]).sameBlockStatistics("FlashchipStat", xw);
            }
            xw.WriteEndElement();	// SSD
            writer.Flush();


            xw.WriteEndElement();  //Result
            writer.Flush();
            /*bool closeResultFile =
                (nextParameters == null) ||
                (nextParameters.outputPath != parameters.outputPath);*/
            if (true)//closeResultFile)
            {
                xw.WriteEndElement(); // results
                xw.WriteEndDocument();
                writer.Close();
                writer = null;
            }


        }
        
        public enum ParameterType { AllocationSchemes, Topology, PlaneNo, DieNo, GCPolicy, EmergencyGCThreshold, WorkloadType, RequestInterArrival, RequestSize, RequestAddress, RequestType };
        public Hashtable investigationList;
        uint largestLSN;
        private void MultiParameterExecutionSyntheticSATA(SSDParameterSet inputParams, SSDParameterSet inputParamsNext)
        {
            summerizedResultFile.WriteLine("Topology\tAllocationScheme\tGC\tIOScheduling\tDieNo\tPlaneNo\tArrivalTime\tAvgReqSize(KB)\tReqAddressDistribution\tWritePercentage"

            + "\tHandledReqs\tHandledReadReqs\tHandledWriteReqs"
            + "\tResponseTime(us)\tMinResponseTime(us)\tMaxResponseTime(us)"
            + "\tReadResponseTime(us)\tMinReadResponseTime(us)\tMaxReadResponseTime(us)"
            + "\tWriteResponseTime(us)\tMinWriteResponseTime(us)\tMaxWriteResponseTime(us)"
            + "\tIOPS\tIOPSReads\tIOPSWrites\tBandWidth(MB/S)"

            + "\tWriteAmplification\tAverageGCCost\tWearLeveling\tAgingRate\tAverageBlockEraseCount\tBlockEraseStdDev\tMaxBlockEraseCount\tMinBlockEraseCount"
            + "\tBlockValidPagesCountAverage\tBlockValidPagesCountStdDev\tBlockValidPagesCountMax\tBlockValidPagesCountMin"
            + "\tBlockInvalidPagesCountAverage\tBlockInvalidPagesCountStdDev\tBlockInvalidPagesCountMax\tBlockInvalidPagesCountMin"
            + "\tAverageNoOfComparisonsToFindCandidateBlock\tAverageNoOfRandomNumberGenerationToCreateRandomSet"
            + "\tEGCExecutionCount\tAverageEGCCost\tAverageEGCTriggerInterval"

            + "\tTotalIssuedReadCMD\tInterleavedReadPercentage\tMultiplaneReadPercentage"
            + "\tTotalIssuedProgramCMD\tInterleavedProgramPercentage\tMultiplaneProgramPercentage\tInterleavedMultiplaneProgramPercentage"
            + "\tTotalIssuedEraseCMD\tInterleavedErasePercentage\tMultiplaneErasePercentage\tInterleavedMultiplaneErasePercentage"
            + "\tAverageCMDLifeTime(us)\tAverageCMDTransferTime(us)\tAverageCMDWaitingTime(us)"
            + "\tAverageReadCMDLifeTime(us)\tAverageReadCMDTransferTime(us)\tAverageReadCMDWaitingTime(us)"
            + "\tAverageProgramCMDLifeTime(us)\tAverageProgramCMDTransferTime(us)\tAverageProgramCMDWaitingTime(us)"
            + "\tChip_TotalExecutionPeriodNet\tChip_TotalExecutionOverlapped\tChip_TotalTransferPeriodNet\tChip_TotalIdlePeriod"

            + "\tAveragePlaneReadCount\tPlaneReadStdDev\tMaxPlaneReadCount\tMinPlaneReadCount"
            + "\tAveragePlaneProgramCount\tPlaneProgramStdDev\tMaxPlaneProgramCount\tMinPlaneProgramCount"
            + "\tAveragePlaneEraseCount\tPlaneEraseStdDev\tMaxPlaneEraseCount\tMinPlaneEraseCount"
            + "\tAveragePlaneFreePagesCount\tPlaneFreePagesStdDev\tMaxPlaneFreePagesCount\tMinPlaneFreePagesCount"
            + "\tAveragePlaneValidPagesCount\tPlaneValidPagesStdDev\tMaxPlaneValidPagesCount\tMinPlaneValidPagesCount"
            + "\tAveragePlaneInvalidPagesCount\tPlaneInvalidPagesStdDev\tMaxPlaneInvalidPagesCount\tMinPlaneInvalidPagesCount");

            double RTNomralizationValue = 0, IOPSNormalizationValue = 0, PlaneReadSTDevNormalizationValue = 0, PlaneProgramSTDevNormalizationValue = 0,
                CMDWaitingTimeNormalizationValue = 0, ReadCMDWaitingTimeNormalizationValue = 0, ProgramCMDWaitingTimeNormalizationValue = 0;
            bool firstValueSet = false;
            if (writeToVerySummerizedFile)
            {
                verySummerizedResultFile.WriteLine("Topology\tAllocationScheme\tGC\tIOScheduling\tDieNo\tPlaneNo\tArrivalTime\tAvgReqSize(KB)\tReqAddressDistribution\tWritePercentage"
                    /*This part can be used if we have different scenarios and we want to normalize critical values to the values of first scenario
                    + "\tNormalizedRT\tNormalizedIOPS"
                    + "\tInterleavedReadPercentage\tMultiplaneReadPercentage"
                    + "\tInterleavedProgramPercentage\tMultiplaneProgramPercentage\tInterleavedMultiplaneProgramPercentage"
                    + "\tNormalizedReadOPSTD\tNormalizedProgramOPSTD"
                    + "\tNormalizedCMDWaitingTime\tNormalizedReadCMDWaitingTime\tNormalizedProgramCMDWaitingTime"*/
                    + "\tResponseTime(us)\tMinResponseTime(us)\tMaxResponseTime(us)"
                    + "\tIOPS"

                    + "\tGCExecutionCount\tWriteAmplification\tAverageGCCost\tWearLeveling\tAgingRate"
                    + "\tAverageBlockEraseCount\tBlockEraseStdDev\tMaxBlockEraseCount\tMinBlockEraseCount"

                    + "\tTotalIssuedReadCMD\tInterleavedReadPercentage\tMultiplaneReadPercentage"
                    + "\tTotalIssuedProgramCMD\tInterleavedProgramPercentage\tMultiplaneProgramPercentage\tInterleavedMultiplaneProgramPercentage"
                    + "\tTotalIssuedEraseCMD\tInterleavedErasePercentage\tMultiplaneErasePercentage\tInterleavedMultiplaneErasePercentage"

                    + "\tAveragePlaneReadCount\tPlaneReadStdDev\tMaxPlaneReadCount\tMinPlaneReadCount"
                    + "\tAveragePlaneProgramCount\tPlaneProgramStdDev\tMaxPlaneProgramCount\tMinPlaneProgramCount"
                    + "\tAveragePlaneEraseCount\tPlaneEraseStdDev\tMaxPlaneEraseCount\tMinPlaneEraseCount"
                    + "\tAveragePlaneFreePagesCount\tPlaneFreePagesStdDev\tMaxPlaneFreePagesCount\tMinPlaneFreePagesCount"
                    + "\tAveragePlaneValidPagesCount\tPlaneValidPagesStdDev\tMaxPlaneValidPagesCount\tMinPlaneValidPagesCount"
                    + "\tAveragePlaneInvalidPagesCount\tPlaneInvalidPagesStdDev\tMaxPlaneInvalidPagesCount\tMinPlaneInvalidPagesCount");
            }

            uint totalNumberOfRequests = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).TotalNumberOfRequests;

            uint[,] topology = new uint[5, 2] { { 4, 4 }, { 8, 8 }, { 6, 4 }, { 4, 8 }, { 4, 16 } };
            uint topologyNo = 2;
            if (!investigationList.ContainsValue(ParameterType.Topology))
            {
                topologyNo = 1;
                topology[0, 0] = inputParams.NetParameters.BusChannelCount;
                topology[0, 1] = inputParams.NetParameters.ChipCountPerChannel;
            }

            PlaneAllocationSchemeType[] allocationSchemes = {
                                       PlaneAllocationSchemeType.CWDP, PlaneAllocationSchemeType.CWPD, PlaneAllocationSchemeType.CDWP, PlaneAllocationSchemeType.CDPW, PlaneAllocationSchemeType.CPWD, PlaneAllocationSchemeType.CPDW,
                                       PlaneAllocationSchemeType.WCDP, PlaneAllocationSchemeType.WCPD, PlaneAllocationSchemeType.WDCP, PlaneAllocationSchemeType.WDPC, PlaneAllocationSchemeType.WPCD, PlaneAllocationSchemeType.WPDC,
                                       PlaneAllocationSchemeType.DCWP, PlaneAllocationSchemeType.DCPW, PlaneAllocationSchemeType.DWCP, PlaneAllocationSchemeType.DWPC, PlaneAllocationSchemeType.DPCW, PlaneAllocationSchemeType.DPWC,
                                       PlaneAllocationSchemeType.PCWD, PlaneAllocationSchemeType.PCDW, PlaneAllocationSchemeType.PWCD, PlaneAllocationSchemeType.PWDC, PlaneAllocationSchemeType.PDCW, PlaneAllocationSchemeType.PDWC,
                                       PlaneAllocationSchemeType.CWD, PlaneAllocationSchemeType.CWP, PlaneAllocationSchemeType.CDW, PlaneAllocationSchemeType.CDP, PlaneAllocationSchemeType.CPW, PlaneAllocationSchemeType.CPD,
                                       PlaneAllocationSchemeType.WCD, PlaneAllocationSchemeType.WCP, PlaneAllocationSchemeType.WDC, PlaneAllocationSchemeType.WDP, PlaneAllocationSchemeType.WPC, PlaneAllocationSchemeType.WPD,
                                       PlaneAllocationSchemeType.DCW, PlaneAllocationSchemeType.DCP, PlaneAllocationSchemeType.DWC, PlaneAllocationSchemeType.DWP, PlaneAllocationSchemeType.DPC, PlaneAllocationSchemeType.DPW,
                                       PlaneAllocationSchemeType.PCW, PlaneAllocationSchemeType.PCD, PlaneAllocationSchemeType.PWC, PlaneAllocationSchemeType.PWD, PlaneAllocationSchemeType.PDC, PlaneAllocationSchemeType.PDW,
                                       PlaneAllocationSchemeType.CW, PlaneAllocationSchemeType.CD, PlaneAllocationSchemeType.CP,                                       
                                       PlaneAllocationSchemeType.WC, PlaneAllocationSchemeType.WD, PlaneAllocationSchemeType.WP,
                                       PlaneAllocationSchemeType.DC, PlaneAllocationSchemeType.DW, PlaneAllocationSchemeType.DP, 
                                       PlaneAllocationSchemeType.PC, PlaneAllocationSchemeType.PW, PlaneAllocationSchemeType.PD,
                                       PlaneAllocationSchemeType.C, PlaneAllocationSchemeType.W, PlaneAllocationSchemeType.D,  PlaneAllocationSchemeType.P,
                                       PlaneAllocationSchemeType.F//Fully dynamic
                                                            };

            uint allocationSchemesNo = 65;
            if (topology[0, 0] == 8)
            {
                PlaneAllocationSchemeType[] myalloc = { PlaneAllocationSchemeType.CWDP, 
                                        PlaneAllocationSchemeType.CPD,
                                        PlaneAllocationSchemeType.CP, PlaneAllocationSchemeType.DP, PlaneAllocationSchemeType.PD,
                                        PlaneAllocationSchemeType.C, PlaneAllocationSchemeType.P,
                                        PlaneAllocationSchemeType.F};
                allocationSchemes = myalloc;
                allocationSchemesNo = 8;
            }
            else
            {
                PlaneAllocationSchemeType[] myalloc = { PlaneAllocationSchemeType.CWDP,
                                        PlaneAllocationSchemeType.CD, PlaneAllocationSchemeType.DC,
                                        PlaneAllocationSchemeType.D, PlaneAllocationSchemeType.P,
                                        PlaneAllocationSchemeType.F};
                allocationSchemes = myalloc;
                allocationSchemesNo = 6;
            }
            /*if (topology[0, 0] == 8)
            {
                PlaneAllocationSchemeType[] myalloc = { PlaneAllocationSchemeType.CWDP,
                                       PlaneAllocationSchemeType.F,//Fully dynamic
                                       PlaneAllocationSchemeType.C, PlaneAllocationSchemeType.CP,
                                       PlaneAllocationSchemeType.P, PlaneAllocationSchemeType.PD};
                allocationSchemes = myalloc;
                allocationSchemesNo = 13;
            }
            else
            {
                PlaneAllocationSchemeType[] myalloc = { PlaneAllocationSchemeType.CWDP,  
                                       PlaneAllocationSchemeType.F,//Fully dynamic
                                       PlaneAllocationSchemeType.CD,
                                       PlaneAllocationSchemeType.WD,
                                       PlaneAllocationSchemeType.D,
                                       PlaneAllocationSchemeType.P};
                allocationSchemes = myalloc;
                allocationSchemesNo = 6;
            }*/
            if (!investigationList.ContainsValue(ParameterType.AllocationSchemes))
            {
                allocationSchemesNo = 1;
                allocationSchemes[0] = inputParams.ControllerParameters.PlaneAllocationScheme;
            }

            uint[] dies = { 1, 2, 4, 6, 8 };
            uint dieNo = 3;
            if (!investigationList.ContainsValue(ParameterType.DieNo))
            {
                dieNo = 1;
                dies[0] = inputParams.FlashChipParameters.dieNoPerChip;
            }

            uint[] planes = { 1, 2, 4, 6, 8 };
            uint planeNo = 3;
            if (!investigationList.ContainsValue(ParameterType.PlaneNo))
            {
                planeNo = 1;
                planes[0] = inputParams.FlashChipParameters.planeNoPerDie;
            }

            uint[] reqInterArrival;
            uint reqInterArrivalNo = 4;
            uint initInterArrivalNo = 1000000;
            if (investigationList.ContainsValue(ParameterType.RequestInterArrival))
            {
                reqInterArrival = new uint[reqInterArrivalNo];
                for (uint i = 0; i < reqInterArrivalNo; i++)
                {
                    reqInterArrival[i] = initInterArrivalNo;
                    initInterArrivalNo *= 10;
                }
            }
            else
            {
                reqInterArrivalNo = 1;
                reqInterArrival = new uint[1];
                reqInterArrival[0] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AverageRequestInterArrivalTime;
            }
            //uint[] normalReqSizeMean = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048};
            uint[] normalReqSizeMean = null;
            uint[] normalReqSizeVariance = null;
            uint requestSizeNo = 0;
            double[] readRatio = null;
            uint readPercentageNo = 0;
            double[,] addressDistribution = null;
            uint reqAddressDistributionNo = 0;

            double[] hotTrafficR = null;
            uint hotTrafficRNo = 0;
            double[] hotTrafficF = null;
            uint hotTrafficFNo = 0;
            if ((inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistType == InputStreamSynthetic.DistributionType.HotCold)
            {
                requestSizeNo = 2;
                normalReqSizeMean = new uint[2] { 16384, 65536 };
                normalReqSizeVariance = new uint[2];
                normalReqSizeVariance[0] = Convert.ToUInt32(normalReqSizeMean[0] * 0.2);
                normalReqSizeVariance[1] = Convert.ToUInt32(normalReqSizeMean[1] * 0.2);


                readPercentageNo = 2;
                readRatio = new double[2];
                readRatio[0] = 0.3;
                readRatio[1] = 0.7;

                hotTrafficF = new double[] { 0.1 };
                hotTrafficFNo = 1;

                hotTrafficR = new double[] { 0.75, 0.85, 0.95};
                hotTrafficRNo = 3;

                reqAddressDistributionNo = hotTrafficRNo * hotTrafficFNo;
                addressDistribution = new double[reqAddressDistributionNo, 2];
                for (uint i = 0; i < hotTrafficFNo; i++)
                    for (uint j = 0; j < hotTrafficRNo; j++)
                    {
                        addressDistribution[i * hotTrafficRNo + j, 0] = hotTrafficF[i];
                        addressDistribution[i * hotTrafficRNo + j, 1] = hotTrafficR[j];
                    }
            }
            else if ((inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistType == InputStreamSynthetic.DistributionType.Uniform)
            {
                requestSizeNo = 10;
                uint initRequestSize = 512;
                if (investigationList.ContainsValue(ParameterType.RequestSize))
                {
                    (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistType = InputStreamSynthetic.DistributionType.Normal;
                    normalReqSizeMean = new uint[requestSizeNo];
                    normalReqSizeVariance = new uint[requestSizeNo];
                    for (uint i = 0; i < requestSizeNo; i++)
                    {
                        normalReqSizeMean[i] = initRequestSize;
                        normalReqSizeVariance[i] = Convert.ToUInt32(initRequestSize * 0.2);
                        initRequestSize *= 2;
                    }
                }
                readRatio = new double[] { 0.3, 0.7 };
                readPercentageNo = 3;

                reqAddressDistributionNo = 5;
                addressDistribution = new double[reqAddressDistributionNo, 2];
                largestLSN = (uint)((inputParams.FlashChipParameters.pageCapacity / FTL.SubPageCapacity) * inputParams.FlashChipParameters.pageNoPerBlock * inputParams.FlashChipParameters.blockNoPerPlane
                    * inputParams.FlashChipParameters.planeNoPerDie * inputParams.FlashChipParameters.dieNoPerChip * inputParams.NetParameters.BusChannelCount *
                    inputParams.NetParameters.ChipCountPerChannel * (1 - inputParams.ControllerParameters.OverprovisionRatio));
                for (uint i = 0; i < reqAddressDistributionNo - 1; i++)
                {
                    addressDistribution[i, 0] = (uint)(((double)largestLSN / 2) * ((double)i / (double)(reqAddressDistributionNo - 1)));
                    addressDistribution[i, 1] = largestLSN - (uint)(((double)largestLSN / 2) * ((double)i / (double)(reqAddressDistributionNo - 1)));
                }
                addressDistribution[reqAddressDistributionNo - 1, 0] = (uint)(largestLSN / 2) - (uint)((double)largestLSN * 0.01);
                addressDistribution[reqAddressDistributionNo - 1, 1] = (uint)(largestLSN / 2) + (uint)((double)largestLSN * 0.01);
            }
            if (!investigationList.ContainsValue(ParameterType.RequestSize))
            {
                requestSizeNo = 1;
                normalReqSizeMean = new uint[1];
                normalReqSizeMean[0] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistParam1;
            }
            if (!investigationList.ContainsValue(ParameterType.RequestType))
            {
                readPercentageNo = 1;
                readRatio = new double[1];
                readRatio[0] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).ReadRatio;
            }
            if (!investigationList.ContainsValue(ParameterType.RequestAddress))
            {
                reqAddressDistributionNo = 1;
                addressDistribution[0, 0] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam1;
                addressDistribution[0, 1] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam2;
            }

            double[,] verySummerizedResults = new double[requestSizeNo, allocationSchemesNo];

            for (uint topologyCntr = 0; topologyCntr < topologyNo; topologyCntr++)
            {
                inputParams.NetParameters.BusChannelCount = topology[topologyCntr, 0];
                inputParams.NetParameters.ChipCountPerChannel = topology[topologyCntr, 1];
                for (uint dieCntr = 0; dieCntr < dieNo; dieCntr++)
                {
                    inputParams.FlashChipParameters.dieNoPerChip = dies[dieCntr];
                    for (uint planeCntr = 0; planeCntr < planeNo; planeCntr++)
                    {
                        inputParams.FlashChipParameters.planeNoPerDie = planes[planeCntr];
                        for (uint reqInterArrivalCntr = 0; reqInterArrivalCntr < reqInterArrivalNo; reqInterArrivalCntr++)
                        {
                            (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AverageRequestInterArrivalTime = reqInterArrival[reqInterArrivalCntr];
                            for (uint readRatioCntr = 0; readRatioCntr < readPercentageNo; readRatioCntr++)
                            {
                                (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).ReadRatio = readRatio[readRatioCntr];
                                for (uint allocationSchemeCntr = 0; allocationSchemeCntr < allocationSchemesNo; allocationSchemeCntr++)
                                {
                                    inputParams.ControllerParameters.PlaneAllocationScheme = allocationSchemes[allocationSchemeCntr];
                                    for (uint reqSizeCntr = 0; reqSizeCntr < requestSizeNo; reqSizeCntr++)
                                    {
                                        //inputParams.ControllerParameters.WorkloadProperties.HostInterfaceType = HostInterface.RequestGeneratorType.SATASynthetic;
                                        (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistParam1 =
                                            normalReqSizeMean[reqSizeCntr];
                                        for (uint reqAddressDistCntr = 0; reqAddressDistCntr < reqAddressDistributionNo; reqAddressDistCntr++)
                                        {
                                            (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam1 = addressDistribution[reqAddressDistCntr, 0];
                                            (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam2 = addressDistribution[reqAddressDistCntr, 1];
                                            IORequest.lastId = 0;
                                            Simulate(inputParams, inputParamsNext);                                            
                    
                                            if (NotListedMessageGenerationParameterSet.Mode == HostInterface.ReqeustGenerationMode.Normal)
                                                verySummerizedResults[reqSizeCntr, allocationSchemeCntr] = SSDController.HostInterface.AvgResponseTime / (double)1000;
                                            else
                                                verySummerizedResults[reqSizeCntr, allocationSchemeCntr] = SSDController.HostInterface.IOPS;

                                            #region WriteToSummerizedResultFile
                                            summerizedResultFile.WriteLine(inputParams.NetParameters.BusChannelCount + "x" + inputParams.NetParameters.ChipCountPerChannel + "\t"
                                                + inputParams.ControllerParameters.PlaneAllocationScheme + "\t"
                                                + NotListedControllerParameterSet.GCProperties.GCPolicy + "\t"
                                                + inputParams.ControllerParameters.SchedulingPolicy + "\t"
                                                + inputParams.FlashChipParameters.dieNoPerChip + "\t"
                                                + inputParams.FlashChipParameters.planeNoPerDie + "\t"
                                            + ((inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AverageRequestInterArrivalTime / 1000000) + "\t"
                                            + ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistParam1 / 2).ToString("F1") + "\t"
                                            + (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistType + "("
                                            + ((inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistType == InputStreamSynthetic.DistributionType.HotCold ?
                                                "f=" + ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam1)
                                                + ",r=" + ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam2)
                                                :
                                                ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam1 / (double)SSDController.FTL.AddressMapper.LargestLSN).ToString("P0") + "-"
                                                + ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam2 / (double)SSDController.FTL.AddressMapper.LargestLSN).ToString("P0")
                                            ) + ")\t"
                                            + (100 - ((int)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).ReadRatio * 100)) + "%\t"

                                            + SSDController.HostInterface.HandledRequestsCount + "\t"
                                            + SSDController.HostInterface.HandledReadRequestsCount + "\t"
                                            + SSDController.HostInterface.HandledWriteRequestsCount + "\t"
                                            + SSDController.HostInterface.AvgResponseTime.ToString() + "\t"
                                            + SSDController.HostInterface.MinResponseTime.ToString() + "\t"
                                            + SSDController.HostInterface.MaxResponseTime.ToString() + "\t"
                                            + SSDController.HostInterface.AvgResponseTimeR.ToString() + "\t"
                                            + SSDController.HostInterface.MinResponseTimeR.ToString() + "\t"
                                            + SSDController.HostInterface.MaxResponseTimeR.ToString() + "\t"
                                            + SSDController.HostInterface.AvgResponseTimeW.ToString() + "\t"
                                            + SSDController.HostInterface.MinResponseTimeW.ToString() + "\t"
                                            + SSDController.HostInterface.MaxResponseTimeW.ToString() + "\t"
                                            + SSDController.HostInterface.IOPS + "\t"
                                            + SSDController.HostInterface.IOPSReads + "\t"
                                            + SSDController.HostInterface.IOPSWrites + "\t"
                                            + SSDController.HostInterface.AggregateBandWidth.ToString() + "\t"

                                            + SSDController.FTL.GarbageCollector.WriteAmplification + "\t" + SSDController.FTL.GarbageCollector.AverageGCCost + "\t"
                                            + SSDController.FTL.GarbageCollector.WearLevelingFairness + "\t" + SSDController.FTL.GarbageCollector.AgingRate + "\t"
                                            + SSDController.FTL.GarbageCollector.BlockEraseCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountStdDev + "\t"
                                            + SSDController.FTL.GarbageCollector.BlockEraseCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountMin + "\t"
                                            + SSDController.FTL.GarbageCollector.BlockValidPagesCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockValidPagesCountStdDev + "\t"
                                            + SSDController.FTL.GarbageCollector.BlockValidPagesCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockValidPagesCountMin + "\t"
                                            + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountStdDev + "\t"
                                            + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountMin + "\t"
                                            + SSDController.FTL.GarbageCollector.AverageNoOfComparisonsToFindCandidateBlock + "\t" + SSDController.FTL.GarbageCollector.AverageNoOfRandomNumberGenerationToCreateRandomSet + "\t"

                                            + (SSDController.FTL.GarbageCollector.EmergencyGCExecutionCount).ToString() + "\t"
                                            + (SSDController.FTL.GarbageCollector.AverageEmergencyGCCost).ToString() + "\t"
                                            + (SSDController.FTL.GarbageCollector.AverageEmergencyGCTriggerInterval).ToString() + "\t"

                                            + (SSDController.FTL.IssuedReadCMD).ToString() + "\t"
                                            + ((double)SSDController.FTL.IssuedInterleaveReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                            + ((double)SSDController.FTL.IssuedMultiplaneReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                            + (SSDController.FTL.IssuedProgramCMD).ToString() + "\t"
                                            + ((double)SSDController.FTL.IssuedInterleaveProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                            + ((double)SSDController.FTL.IssuedMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                            + ((double)SSDController.FTL.IssuedInterleaveMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                            + (SSDController.FTL.IssuedEraseCMD).ToString() + "\t"
                                            + (SSDController.FTL.IssuedInterleaveEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"
                                            + (SSDController.FTL.IssuedMultiplaneEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"
                                            + (SSDController.FTL.IssuedInterleaveMultiplaneEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"
                                            + SSDController.HostInterface.AverageCMDLifeTime.ToString() + "\t"
                                            + SSDController.HostInterface.AverageCMDTransferTime.ToString() + "\t"
                                            + SSDController.HostInterface.AverageCMDWaitingTime.ToString() + "\t"
                                            + SSDController.HostInterface.AverageReadCMDLifeTime.ToString() + "\t"
                                            + SSDController.HostInterface.AverageReadCMDTransferTime.ToString() + "\t"
                                            + SSDController.HostInterface.AverageReadCMDWaitingTime.ToString() + "\t"
                                            + SSDController.HostInterface.AverageProgramCMDLifeTime.ToString() + "\t"
                                            + SSDController.HostInterface.AverageProgramCMDTransferTime.ToString() + "\t"
                                            + SSDController.HostInterface.AverageProgramCMDWaitingTime.ToString() + "\t"
                                            + SSDController.FTL.AverageFlashChipCMDExecutionPeriodNet.ToString() + "\t"
                                            + SSDController.FTL.AverageFlashChipCMDExecutionPeriodOverlapped.ToString() + "\t"
                                            + SSDController.FTL.AverageFlashChipTransferPeriodNet.ToString() + "\t"
                                            + SSDController.FTL.AverageFlashChipeIdePeriod.ToString() + "\t"

                                            + SSDController.FTL.AveragePageReadsPerPlane + "\t" + SSDController.FTL.PlanePageReadsStdDev + "\t" + SSDController.FTL.MaxPlaneReadCount + "\t" + SSDController.FTL.MinPlaneReadCount + "\t"
                                            + SSDController.FTL.AveragePageProgramsPerPlane + "\t" + SSDController.FTL.PlanePageProgramsStdDev + "\t" + SSDController.FTL.MaxPlaneProgramCount + "\t" + SSDController.FTL.MinPlaneProgramCount + "\t"
                                            + SSDController.FTL.AverageBlockErasesPerPlane + "\t" + SSDController.FTL.PlaneBlockErasesStdDev + "\t" + SSDController.FTL.MaxPlaneEraseCount + "\t" + SSDController.FTL.MinPlaneEraseCount + "\t"
                                            + SSDController.FTL.AverageNumberOfFreePagesPerPlane + "\t" + SSDController.FTL.PlaneFreePagesStdDev + "\t" + SSDController.FTL.MaxPlaneFreePagesCount + "\t" + SSDController.FTL.MinPlaneFreePagesCount + "\t"
                                            + SSDController.FTL.AverageNumberOfValidPagesPerPlane + "\t" + SSDController.FTL.PlaneValidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneValidPagesCount + "\t" + SSDController.FTL.MinPlaneValidPagesCount + "\t"
                                            + SSDController.FTL.AverageNumberOfInvalidPagesPerPlane + "\t" + SSDController.FTL.PlaneInvalidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneInvalidPagesCount + "\t" + SSDController.FTL.MinPlaneInvalidPagesCount);
                                            summerizedResultFile.Flush();
                                            #endregion

                                            #region WriteToVerySummarizedResultFile
                                            if (writeToVerySummerizedFile)
                                            {
                                                if (!firstValueSet)
                                                {
                                                    firstValueSet = true;
                                                    RTNomralizationValue = SSDController.HostInterface.AvgResponseTime;
                                                    if (RTNomralizationValue == 0)
                                                        RTNomralizationValue = 1;
                                                    IOPSNormalizationValue = SSDController.HostInterface.IOPS;
                                                    if (IOPSNormalizationValue == 0)
                                                        IOPSNormalizationValue = 1;
                                                    PlaneReadSTDevNormalizationValue = SSDController.FTL.PlanePageReadsStdDev;
                                                    if (PlaneReadSTDevNormalizationValue == 0)
                                                        PlaneReadSTDevNormalizationValue = 1;
                                                    PlaneProgramSTDevNormalizationValue = SSDController.FTL.PlanePageProgramsStdDev;
                                                    if (PlaneProgramSTDevNormalizationValue == 0)
                                                        PlaneProgramSTDevNormalizationValue = 1;
                                                    CMDWaitingTimeNormalizationValue = SSDController.HostInterface.AverageCMDWaitingTime;
                                                    if (CMDWaitingTimeNormalizationValue == 0)
                                                        CMDWaitingTimeNormalizationValue = 1;
                                                    ReadCMDWaitingTimeNormalizationValue = SSDController.HostInterface.AverageReadCMDWaitingTime;
                                                    if (ReadCMDWaitingTimeNormalizationValue == 0)
                                                        ReadCMDWaitingTimeNormalizationValue = 1;
                                                    ProgramCMDWaitingTimeNormalizationValue = SSDController.HostInterface.AverageProgramCMDWaitingTime;
                                                    if (ProgramCMDWaitingTimeNormalizationValue == 0)
                                                        ProgramCMDWaitingTimeNormalizationValue = 1;
                                                }
                                                verySummerizedResultFile.WriteLine(inputParams.NetParameters.BusChannelCount + "x" + inputParams.NetParameters.ChipCountPerChannel + "\t"
                                                    + inputParams.ControllerParameters.PlaneAllocationScheme + "\t"
                                                    + NotListedControllerParameterSet.GCProperties.GCPolicy + "\t"
                                                    + inputParams.ControllerParameters.SchedulingPolicy + "\t"
                                                    + inputParams.FlashChipParameters.dieNoPerChip + "\t"
                                                    + inputParams.FlashChipParameters.planeNoPerDie + "\t"
                                                    + ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistParam1 / 2).ToString("F1") + "\t"
                                                    + (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistType + "("
                                                    + ((inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistType == InputStreamSynthetic.DistributionType.HotCold ?
                                                    "f=" + ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam1 / (double)100)
                                                    + ",r=" + ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam2 / (double)100)
                                                    :
                                                    ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam1 / (double)SSDController.FTL.AddressMapper.LargestLSN).ToString("P0") + "-"
                                                    + ((double)(inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AddressDistParam2 / (double)SSDController.FTL.AddressMapper.LargestLSN).ToString("P0")
                                                ) + ")\t"
                                                + (100 - (uint)((inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).ReadRatio * 100)) + "%\t"

                                                /*This part can be used if we have different scenarios and we want to normalize critical values to the values of first scenario
                                                + (SSDController.HostInterface.AvgResponseTime / RTNomralizationValue) + "\t"
                                                + (SSDController.HostInterface.IOPS / IOPSNormalizationValue) + "\t"
                                                + ((double)SSDController.FTL.IssuedInterleaveReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedMultiplaneReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedInterleaveProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedInterleaveMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                                + (SSDController.FTL.PlanePageReadsStdDev / PlaneReadSTDevNormalizationValue) + "\t"
                                                + (SSDController.FTL.PlanePageProgramsStdDev / PlaneProgramSTDevNormalizationValue) + "\t"
                                                + (SSDController.HostInterface.AverageCMDWaitingTime / CMDWaitingTimeNormalizationValue) + "\t"
                                                + (SSDController.HostInterface.AverageReadCMDWaitingTime / ReadCMDWaitingTimeNormalizationValue) + "\t"
                                                + (SSDController.HostInterface.AverageProgramCMDWaitingTime / ProgramCMDWaitingTimeNormalizationValue) + "\t"*/

                                                + SSDController.HostInterface.AvgResponseTime.ToString() + "\t"
                                                + SSDController.HostInterface.MinResponseTime.ToString() + "\t"
                                                + SSDController.HostInterface.MaxResponseTime.ToString() + "\t"
                                                + SSDController.HostInterface.IOPS + "\t"

                                                + SSDController.FTL.GarbageCollector.TotalGCExecutionCount + "\t"
                                                + SSDController.FTL.GarbageCollector.WriteAmplification + "\t" + SSDController.FTL.GarbageCollector.AverageGCCost + "\t"
                                                + SSDController.FTL.GarbageCollector.WearLevelingFairness + "\t" + SSDController.FTL.GarbageCollector.AgingRate + "\t"
                                                + SSDController.FTL.GarbageCollector.BlockEraseCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountStdDev + "\t"
                                                + SSDController.FTL.GarbageCollector.BlockEraseCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountMin + "\t"

                                                + (SSDController.FTL.IssuedReadCMD).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedInterleaveReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedMultiplaneReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                                + (SSDController.FTL.IssuedProgramCMD).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedInterleaveProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                                + ((double)SSDController.FTL.IssuedInterleaveMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                                + (SSDController.FTL.IssuedEraseCMD).ToString() + "\t"
                                                + (SSDController.FTL.IssuedInterleaveEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"
                                                + (SSDController.FTL.IssuedMultiplaneEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"
                                                + (SSDController.FTL.IssuedInterleaveMultiplaneEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"

                                                + SSDController.FTL.AveragePageReadsPerPlane + "\t" + SSDController.FTL.PlanePageReadsStdDev + "\t" + SSDController.FTL.MaxPlaneReadCount + "\t" + SSDController.FTL.MinPlaneReadCount + "\t"
                                                + SSDController.FTL.AveragePageProgramsPerPlane + "\t" + SSDController.FTL.PlanePageProgramsStdDev + "\t" + SSDController.FTL.MaxPlaneProgramCount + "\t" + SSDController.FTL.MinPlaneProgramCount + "\t"
                                                + SSDController.FTL.AverageBlockErasesPerPlane + "\t" + SSDController.FTL.PlaneBlockErasesStdDev + "\t" + SSDController.FTL.MaxPlaneEraseCount + "\t" + SSDController.FTL.MinPlaneEraseCount + "\t"
                                                + SSDController.FTL.AverageNumberOfFreePagesPerPlane + "\t" + SSDController.FTL.PlaneFreePagesStdDev + "\t" + SSDController.FTL.MaxPlaneFreePagesCount + "\t" + SSDController.FTL.MinPlaneFreePagesCount + "\t"
                                                + SSDController.FTL.AverageNumberOfValidPagesPerPlane + "\t" + SSDController.FTL.PlaneValidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneValidPagesCount + "\t" + SSDController.FTL.MinPlaneValidPagesCount + "\t"
                                                + SSDController.FTL.AverageNumberOfInvalidPagesPerPlane + "\t" + SSDController.FTL.PlaneInvalidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneInvalidPagesCount + "\t" + SSDController.FTL.MinPlaneInvalidPagesCount);
                                                verySummerizedResultFile.Flush();
                                            }
                                            #endregion

                                            SSDController.HostInterface = null;
                                            SSDController = null;
                                            XEngineFactory.XEngine.Reset();
                                            GC.Collect();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            #region WriteToVerySummarizedResultFile
            /*verySummerizedResultFile.Write("\t");
            for (int i = 0; i < allocationSchemesNo; i++)
            {
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            }
            for (int i = 0; i < 24; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            for (int i = 29; i < 35; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            for (int i = 39; i < 45; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            for (int i = 49; i < 55; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            for (int i = 59; i < 65; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            for (int i = 26; i < 29; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            for (int i = 36; i < 39; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            for (int i = 46; i < 49; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            for (int i = 56; i < 59; i++)
                verySummerizedResultFile.Write(allocationSchemes[i].ToString() + "\t");
            verySummerizedResultFile.Write(allocationSchemes[25].ToString() + "\t");
            verySummerizedResultFile.Write(allocationSchemes[35].ToString() + "\t");
            verySummerizedResultFile.Write(allocationSchemes[45].ToString() + "\t");
            verySummerizedResultFile.Write(allocationSchemes[55].ToString() + "\t");
            verySummerizedResultFile.Write(allocationSchemes[24].ToString() + "\t");
            verySummerizedResultFile.Write("\n");

            for (int i = 0; i < requestSizeNo; i++)
            {
                verySummerizedResultFile.Write("{0:F1}\t", (((double)normalReqSizeMean[i]) / 1024));
                for (int j = 0; j < allocationSchemesNo; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 0; j < 24; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 29; j < 35; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 39; j < 45; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 49; j < 55; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 59; j < 65; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 26; j < 29; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 36; j < 39; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 46; j < 49; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                for (int j = 56; j < 59; j++)
                    verySummerizedResultFile.Write(verySummerizedResults[i, j].ToString() + "\t");
                verySummerizedResultFile.Write(verySummerizedResults[i, 25].ToString() + "\t");
                verySummerizedResultFile.Write(verySummerizedResults[i, 35].ToString() + "\t");
                verySummerizedResultFile.Write(verySummerizedResults[i, 45].ToString() + "\t");
                verySummerizedResultFile.Write(verySummerizedResults[i, 55].ToString() + "\t");
                verySummerizedResultFile.Write(verySummerizedResults[i, 24].ToString() + "\t");
                verySummerizedResultFile.Write("\n");
            }*/
            #endregion
        }
        private void MultiParameterExecutionTraceBasedSingleFlow(SSDParameterSet inputParams, SSDParameterSet inputParamsNext)
        {
            summerizedResultFile.WriteLine("Topology\tAllocationScheme\tGC\tIOSchedulingPolicy\tDieNo\tPlaneNo\tTraceName\tPercentage\tMode"

            + "\tRatioOfIgnoredRequests"
            + "\tHandledReqs\tHandledReadReqs\tHandledWriteReqs"
            + "\tResponseTime(us)\tMinResponseTime(us)\tMaxResponseTime(us)"
            + "\tReadResponseTime(us)\tMinReadResponseTime(us)\tMaxReadResponseTime(us)"
            + "\tWriteResponseTime(us)\tMinWriteResponseTime(us)\tMaxWriteResponseTime(us)"
            + "\tIOPS\tIOPSReads\tIOPSWrites\tBandWidth(MB/S)"

            + "\tWriteAmplification\tAverageGCCost\tWearLeveling\tAgingRate\tAverageBlockEraseCount\tBlockEraseStdDev\tMaxBlockEraseCount\tMinBlockEraseCount"
            + "\tBlockValidPagesCountAverage\tBlockValidPagesCountStdDev\tBlockValidPagesCountMax\tBlockValidPagesCountMin"
            + "\tBlockInvalidPagesCountAverage\tBlockInvalidPagesCountStdDev\tBlockInvalidPagesCountMax\tBlockInvalidPagesCountMin"
            + "\tAverageNoOfComparisonsToFindCandidateBlock\tAverageNoOfRandomNumberGenerationToCreateRandomSet"
            + "\tEGCExecutionCount\tAverageEGCCost\tAverageEGCTriggerInterval"

            + "\tTotalIssuedReadCMD\tInterleavedReadPercentage\tMultiplaneReadPercentage"
            + "\tTotalIssuedProgramCMD\tInterleavedProgramPercentage\tMultiplaneProgramPercentage\tInterleavedMultiplaneProgramPercentage"
            + "\tTotalIssuedEraseCMD\tInterleavedErasePercentage\tMultiplaneErasePercentage\tInterleavedMultiplaneErasePercentage"
            + "\tAverageCMDLifeTime(us)\tAverageCMDTransferTime(us)\tAverageCMDWaitingTime(us)"
            + "\tAverageReadCMDLifeTime(us)\tAverageReadCMDTransferTime(us)\tAverageReadCMDWaitingTime(us)"
            + "\tAverageProgramCMDLifeTime(us)\tAverageProgramCMDTransferTime(us)\tAverageProgramCMDWaitingTime(us)"
            + "\tChip_TotalExecutionPeriodNet\tChip_TotalExecutionOverlapped\tChip_TotalTransferPeriodNet\tChip_TotalIdlePeriod"

            + "\tAveragePlaneReadCount\tPlaneReadStdDev\tMaxPlaneReadCount\tMinPlaneReadCount"
            + "\tAveragePlaneProgramCount\tPlaneProgramStdDev\tMaxPlaneProgramCount\tMinPlaneProgramCount"
            + "\tAveragePlaneEraseCount\tPlaneEraseStdDev\tMaxPlaneEraseCount\tMinPlaneEraseCount"
            + "\tAveragePlaneFreePagesCount\tPlaneFreePagesStdDev\tMaxPlaneFreePagesCount\tMinPlaneFreePagesCount"
            + "\tAveragePlaneValidPagesCount\tPlaneValidPagesStdDev\tMaxPlaneValidPagesCount\tMinPlaneValidPagesCount"
            + "\tAveragePlaneInvalidPagesCount\tPlaneInvalidPagesStdDev\tMaxPlaneInvalidPagesCount\tMinPlaneInvalidPagesCount");

            double RTNomralizationValue = 0, IOPSNormalizationValue = 0, PlaneReadSTDevNormalizationValue = 0, PlaneProgramSTDevNormalizationValue = 0,
                CMDWaitingTimeNormalizationValue = 0, ReadCMDWaitingTimeNormalizationValue = 0, ProgramCMDWaitingTimeNormalizationValue = 0;
            bool firstValueSet = false;
            if (writeToVerySummerizedFile)
            {
                verySummerizedResultFile.WriteLine("Topology\tAllocationScheme\tGC\tIOSchedulingPolicy\tDieNo\tPlaneNo\tTraceName\tPercentage\tMode"
                    /*This part can be used if we have different scenarios and we want to normalize critical values to the values of first scenario
                    + "\tNormalizedRT\tNormalizedIOPS"
                    + "\tInterleavedReadPercentage\tMultiplaneReadPercentage"
                    + "\tInterleavedProgramPercentage\tMultiplaneProgramPercentage\tInterleavedMultiplaneProgramPercentage"
                    + "\tNormalizedReadOPSTD\tNormalizedProgramOPSTD"
                    + "\tNormalizedCMDWaitingTime\tNormalizedReadCMDWaitingTime\tNormalizedProgramCMDWaitingTime"*/

                + "\tResponseTime(us)\tMinResponseTime(us)\tMaxResponseTime(us)"
                + "\tIOPS"

                + "\tGCExecutionCount\tWriteAmplification\tAverageGCCost\tWearLeveling\tAgingRate"
                + "\tAverageBlockEraseCount\tBlockEraseStdDev\tMaxBlockEraseCount\tMinBlockEraseCount"

                + "\tTotalIssuedReadCMD\tInterleavedReadPercentage\tMultiplaneReadPercentage"
                + "\tTotalIssuedProgramCMD\tInterleavedProgramPercentage\tMultiplaneProgramPercentage\tInterleavedMultiplaneProgramPercentage"
                + "\tTotalIssuedEraseCMD\tInterleavedErasePercentage\tMultiplaneErasePercentage\tInterleavedMultiplaneErasePercentage"

                + "\tAveragePlaneReadCount\tPlaneReadStdDev\tMaxPlaneReadCount\tMinPlaneReadCount"
                + "\tAveragePlaneProgramCount\tPlaneProgramStdDev\tMaxPlaneProgramCount\tMinPlaneProgramCount"
                + "\tAveragePlaneEraseCount\tPlaneEraseStdDev\tMaxPlaneEraseCount\tMinPlaneEraseCount"
                + "\tAveragePlaneFreePagesCount\tPlaneFreePagesStdDev\tMaxPlaneFreePagesCount\tMinPlaneFreePagesCount"
                + "\tAveragePlaneValidPagesCount\tPlaneValidPagesStdDev\tMaxPlaneValidPagesCount\tMinPlaneValidPagesCount"
                + "\tAveragePlaneInvalidPagesCount\tPlaneInvalidPagesStdDev\tMaxPlaneInvalidPagesCount\tMinPlaneInvalidPagesCount");
            }

            uint[,] topology = new uint[5, 2] { { 4, 4 }, { 8, 8 }, { 6, 4 }, { 4, 8 }, { 4, 16 } };
            uint topologyNo = 2;
            if (!investigationList.ContainsValue(ParameterType.Topology))
            {
                topologyNo = 1;
                topology[0, 0] = inputParams.NetParameters.BusChannelCount;
                topology[0, 1] = inputParams.NetParameters.ChipCountPerChannel;
            }

            PlaneAllocationSchemeType[] allocationSchemes = {
                                       PlaneAllocationSchemeType.CWDP, PlaneAllocationSchemeType.CWPD, PlaneAllocationSchemeType.CDWP, PlaneAllocationSchemeType.CDPW, PlaneAllocationSchemeType.CPWD, PlaneAllocationSchemeType.CPDW,
                                       PlaneAllocationSchemeType.WCDP, PlaneAllocationSchemeType.WCPD, PlaneAllocationSchemeType.WDCP, PlaneAllocationSchemeType.WDPC, PlaneAllocationSchemeType.WPCD, PlaneAllocationSchemeType.WPDC,
                                       PlaneAllocationSchemeType.DCWP, PlaneAllocationSchemeType.DCPW, PlaneAllocationSchemeType.DWCP, PlaneAllocationSchemeType.DWPC, PlaneAllocationSchemeType.DPCW, PlaneAllocationSchemeType.DPWC,
                                       PlaneAllocationSchemeType.PCWD, PlaneAllocationSchemeType.PCDW, PlaneAllocationSchemeType.PWCD, PlaneAllocationSchemeType.PWDC, PlaneAllocationSchemeType.PDCW, PlaneAllocationSchemeType.PDWC,
                                       PlaneAllocationSchemeType.CWD, PlaneAllocationSchemeType.CWP, PlaneAllocationSchemeType.CDW, PlaneAllocationSchemeType.CDP, PlaneAllocationSchemeType.CPW, PlaneAllocationSchemeType.CPD,
                                       PlaneAllocationSchemeType.WCD, PlaneAllocationSchemeType.WCP, PlaneAllocationSchemeType.WDC, PlaneAllocationSchemeType.WDP, PlaneAllocationSchemeType.WPC, PlaneAllocationSchemeType.WPD,
                                       PlaneAllocationSchemeType.DCW, PlaneAllocationSchemeType.DCP, PlaneAllocationSchemeType.DWC, PlaneAllocationSchemeType.DWP, PlaneAllocationSchemeType.DPC, PlaneAllocationSchemeType.DPW,
                                       PlaneAllocationSchemeType.PCW, PlaneAllocationSchemeType.PCD, PlaneAllocationSchemeType.PWC, PlaneAllocationSchemeType.PWD, PlaneAllocationSchemeType.PDC, PlaneAllocationSchemeType.PDW,
                                       PlaneAllocationSchemeType.CW, PlaneAllocationSchemeType.CD, PlaneAllocationSchemeType.CP,                                       
                                       PlaneAllocationSchemeType.WC, PlaneAllocationSchemeType.WD, PlaneAllocationSchemeType.WP,
                                       PlaneAllocationSchemeType.DC, PlaneAllocationSchemeType.DW, PlaneAllocationSchemeType.DP, 
                                       PlaneAllocationSchemeType.PC, PlaneAllocationSchemeType.PW, PlaneAllocationSchemeType.PD,
                                       PlaneAllocationSchemeType.C, PlaneAllocationSchemeType.W, PlaneAllocationSchemeType.D,  PlaneAllocationSchemeType.P,
                                       PlaneAllocationSchemeType.F//Fully dynamic
                                                            };
                                                            
            uint allocationSchemesNo = 65;
            if (topology[0, 0] == 8)
            {
                PlaneAllocationSchemeType[] myalloc = { PlaneAllocationSchemeType.CWDP, 
                                        PlaneAllocationSchemeType.CPD,
                                        PlaneAllocationSchemeType.CP, PlaneAllocationSchemeType.DP, PlaneAllocationSchemeType.PD,
                                        PlaneAllocationSchemeType.C, PlaneAllocationSchemeType.P,
                                        PlaneAllocationSchemeType.F};
                allocationSchemes = myalloc;
                allocationSchemesNo = 8;
            }
            else
            {
                PlaneAllocationSchemeType[] myalloc = { PlaneAllocationSchemeType.CWDP,
                                        PlaneAllocationSchemeType.CD, PlaneAllocationSchemeType.DC,
                                        PlaneAllocationSchemeType.D, PlaneAllocationSchemeType.P,
                                        PlaneAllocationSchemeType.F};
                allocationSchemes = myalloc;
                allocationSchemesNo = 6;
            }
            if (!investigationList.ContainsValue(ParameterType.AllocationSchemes))
            {
                allocationSchemesNo = 1;
                allocationSchemes[0] = inputParams.ControllerParameters.PlaneAllocationScheme;
            }

            uint[] dies = { 1, 2, 4, 6, 8 };
            uint dieNo = 3;
            if (!investigationList.ContainsValue(ParameterType.DieNo))
            {
                dieNo = 1;
                dies[0] = inputParams.FlashChipParameters.dieNoPerChip;
            }

            uint[] planes = { 1, 2, 4, 6, 8 };
            uint planeNo = 3;
            if (!investigationList.ContainsValue(ParameterType.PlaneNo))
            {
                planeNo = 1;
                planes[0] = inputParams.FlashChipParameters.planeNoPerDie;
            }

            for (uint topologyCntr = 0; topologyCntr < topologyNo; topologyCntr++)
            {
                inputParams.NetParameters.BusChannelCount = topology[topologyCntr, 0];
                inputParams.NetParameters.ChipCountPerChannel = topology[topologyCntr, 1];
                for (uint allocationSchemeCntr = 0; allocationSchemeCntr < allocationSchemesNo; allocationSchemeCntr++)
                {
                    inputParams.ControllerParameters.PlaneAllocationScheme = allocationSchemes[allocationSchemeCntr];
                    for (uint dieCntr = 0; dieCntr < dieNo; dieCntr++)
                    {
                        inputParams.FlashChipParameters.dieNoPerChip = dies[dieCntr];
                        for (uint planeCntr = 0; planeCntr < planeNo; planeCntr++)
                        {
                            inputParams.FlashChipParameters.planeNoPerDie = planes[planeCntr];
                            IORequest.lastId = 0;
                            Simulate(inputParams, inputParamsNext);

                            switch (inputParams.ControllerParameters.HostInterfaceType)
                            {
                                case HostInterface.HostInterfaceType.SATATraceBased:
                                    #region WriteToSummarizedResultFile
                                    summerizedResultFile.WriteLine(inputParams.NetParameters.BusChannelCount + "x" + inputParams.NetParameters.ChipCountPerChannel + "\t"
                                        + inputParams.ControllerParameters.PlaneAllocationScheme + "\t"
                                        + NotListedControllerParameterSet.GCProperties.GCPolicy + "\t"
                                        + inputParams.ControllerParameters.SchedulingPolicy + "\t"
                                        + inputParams.FlashChipParameters.dieNoPerChip + "\t"
                                        + inputParams.FlashChipParameters.planeNoPerDie + "\t"
                                    + (inputParams.ControllerParameters.Workloads[0] as TraceBasedParameterSet).FilePath.Remove(0, (inputParams.ControllerParameters.Workloads[0] as TraceBasedParameterSet).FilePath.LastIndexOf("\\") + 1) + "\t"
                                    + (inputParams.ControllerParameters.Workloads[0] as TraceBasedParameterSet).PercentageToBeSimulated + "%\t"
                                    + NotListedMessageGenerationParameterSet.Mode + "\t"

                                    + SSDController.HostInterface.RatioOfIgnoredRequests + "\t"
                                    + SSDController.HostInterface.HandledRequestsCount + "\t"
                                    + SSDController.HostInterface.HandledReadRequestsCount + "\t"
                                    + SSDController.HostInterface.HandledWriteRequestsCount + "\t"
                                    + SSDController.HostInterface.AvgResponseTime.ToString() + "\t"
                                    + SSDController.HostInterface.MinResponseTime.ToString() + "\t"
                                    + SSDController.HostInterface.MaxResponseTime.ToString() + "\t"
                                    + SSDController.HostInterface.AvgResponseTimeR.ToString() + "\t"
                                    + SSDController.HostInterface.MinResponseTimeR.ToString() + "\t"
                                    + SSDController.HostInterface.MaxResponseTimeR.ToString() + "\t"
                                    + SSDController.HostInterface.AvgResponseTimeW.ToString() + "\t"
                                    + SSDController.HostInterface.MinResponseTimeW.ToString() + "\t"
                                    + SSDController.HostInterface.MaxResponseTimeW.ToString() + "\t"
                                    + SSDController.HostInterface.IOPS + "\t"
                                    + SSDController.HostInterface.IOPSReads + "\t"
                                    + SSDController.HostInterface.IOPSWrites + "\t"
                                    + SSDController.HostInterface.AggregateBandWidth.ToString() + "\t"

                                    + SSDController.FTL.GarbageCollector.WriteAmplification + "\t" + SSDController.FTL.GarbageCollector.AverageGCCost + "\t"
                                    + SSDController.FTL.GarbageCollector.WearLevelingFairness + "\t" + SSDController.FTL.GarbageCollector.AgingRate + "\t"
                                    + SSDController.FTL.GarbageCollector.BlockEraseCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountStdDev + "\t"
                                    + SSDController.FTL.GarbageCollector.BlockEraseCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountMin + "\t"
                                    + SSDController.FTL.GarbageCollector.BlockValidPagesCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockValidPagesCountStdDev + "\t"
                                    + SSDController.FTL.GarbageCollector.BlockValidPagesCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockValidPagesCountMin + "\t"
                                    + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountStdDev + "\t"
                                    + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountMin + "\t"
                                    + SSDController.FTL.GarbageCollector.AverageNoOfComparisonsToFindCandidateBlock + "\t" + SSDController.FTL.GarbageCollector.AverageNoOfRandomNumberGenerationToCreateRandomSet + "\t"

                                    + (SSDController.FTL.GarbageCollector.EmergencyGCExecutionCount).ToString() + "\t"
                                    + (SSDController.FTL.GarbageCollector.AverageEmergencyGCCost).ToString() + "\t"
                                    + (SSDController.FTL.GarbageCollector.AverageEmergencyGCTriggerInterval).ToString() + "\t"

                                    + (SSDController.FTL.IssuedReadCMD).ToString() + "\t"
                                    + ((double)SSDController.FTL.IssuedInterleaveReadCMD / (double)SSDController.FTL.IssuedReadCMD * 100).ToString() + "\t"
                                    + ((double)SSDController.FTL.IssuedMultiplaneReadCMD / (double)SSDController.FTL.IssuedReadCMD * 100).ToString() + "\t"
                                    + (SSDController.FTL.IssuedProgramCMD).ToString() + "\t"
                                    + ((double)SSDController.FTL.IssuedInterleaveProgramCMD / (double)SSDController.FTL.IssuedProgramCMD * 100).ToString() + "\t"
                                    + ((double)SSDController.FTL.IssuedMultiplaneProgramCMD / (double)SSDController.FTL.IssuedProgramCMD * 100).ToString() + "\t"
                                    + ((double)SSDController.FTL.IssuedInterleaveMultiplaneProgramCMD / (double)SSDController.FTL.IssuedProgramCMD * 100).ToString() + "\t"
                                    + (SSDController.FTL.IssuedEraseCMD).ToString() + "\t"
                                    + (SSDController.FTL.IssuedInterleaveEraseCMD / (double)SSDController.FTL.IssuedEraseCMD * 100).ToString() + "\t"
                                    + (SSDController.FTL.IssuedMultiplaneEraseCMD / (double)SSDController.FTL.IssuedEraseCMD * 100).ToString() + "\t"
                                    + (SSDController.FTL.IssuedInterleaveMultiplaneEraseCMD / (double)SSDController.FTL.IssuedEraseCMD * 100).ToString() + "\t"
                                    + SSDController.HostInterface.AverageCMDLifeTime.ToString() + "\t"
                                    + SSDController.HostInterface.AverageCMDTransferTime.ToString() + "\t"
                                    + SSDController.HostInterface.AverageCMDWaitingTime.ToString() + "\t"
                                    + SSDController.HostInterface.AverageReadCMDLifeTime.ToString() + "\t"
                                    + SSDController.HostInterface.AverageReadCMDTransferTime.ToString() + "\t"
                                    + SSDController.HostInterface.AverageReadCMDWaitingTime.ToString() + "\t"
                                    + SSDController.HostInterface.AverageProgramCMDLifeTime.ToString() + "\t"
                                    + SSDController.HostInterface.AverageProgramCMDTransferTime.ToString() + "\t"
                                    + SSDController.HostInterface.AverageProgramCMDWaitingTime.ToString() + "\t"
                                    + SSDController.FTL.AverageFlashChipCMDExecutionPeriodNet.ToString() + "\t"
                                    + SSDController.FTL.AverageFlashChipCMDExecutionPeriodOverlapped.ToString() + "\t"
                                    + SSDController.FTL.AverageFlashChipTransferPeriodNet.ToString() + "\t"
                                    + SSDController.FTL.AverageFlashChipeIdePeriod.ToString() + "\t"

                                    + SSDController.FTL.AveragePageReadsPerPlane + "\t" + SSDController.FTL.PlanePageReadsStdDev + "\t" + SSDController.FTL.MaxPlaneReadCount + "\t" + SSDController.FTL.MinPlaneReadCount + "\t"
                                    + SSDController.FTL.AveragePageProgramsPerPlane + "\t" + SSDController.FTL.PlanePageProgramsStdDev + "\t" + SSDController.FTL.MaxPlaneProgramCount + "\t" + SSDController.FTL.MinPlaneProgramCount + "\t"
                                    + SSDController.FTL.AverageBlockErasesPerPlane + "\t" + SSDController.FTL.PlaneBlockErasesStdDev + "\t" + SSDController.FTL.MaxPlaneEraseCount + "\t" + SSDController.FTL.MinPlaneEraseCount + "\t"
                                    + SSDController.FTL.AverageNumberOfFreePagesPerPlane + "\t" + SSDController.FTL.PlaneFreePagesStdDev + "\t" + SSDController.FTL.MaxPlaneFreePagesCount + "\t" + SSDController.FTL.MinPlaneFreePagesCount + "\t"
                                    + SSDController.FTL.AverageNumberOfValidPagesPerPlane + "\t" + SSDController.FTL.PlaneValidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneValidPagesCount + "\t" + SSDController.FTL.MinPlaneValidPagesCount + "\t"
                                    + SSDController.FTL.AverageNumberOfInvalidPagesPerPlane + "\t" + SSDController.FTL.PlaneInvalidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneInvalidPagesCount + "\t" + SSDController.FTL.MinPlaneInvalidPagesCount);
                                    #endregion

                                    #region WriteToVerySummarizedResultFile
                                    if (writeToVerySummerizedFile)
                                    {
                                        if (!firstValueSet)
                                        {
                                            firstValueSet = true;
                                            RTNomralizationValue = SSDController.HostInterface.AvgResponseTime;
                                            if (RTNomralizationValue == 0)
                                                RTNomralizationValue = 1;
                                            IOPSNormalizationValue = SSDController.HostInterface.IOPS;
                                            if (IOPSNormalizationValue == 0)
                                                IOPSNormalizationValue = 1;
                                            PlaneReadSTDevNormalizationValue = SSDController.FTL.PlanePageReadsStdDev;
                                            if (PlaneReadSTDevNormalizationValue == 0)
                                                PlaneReadSTDevNormalizationValue = 1;
                                            PlaneProgramSTDevNormalizationValue = SSDController.FTL.PlanePageProgramsStdDev;
                                            if (PlaneProgramSTDevNormalizationValue == 0)
                                                PlaneProgramSTDevNormalizationValue = 1;
                                            CMDWaitingTimeNormalizationValue = SSDController.HostInterface.AverageCMDWaitingTime;
                                            if (CMDWaitingTimeNormalizationValue == 0)
                                                CMDWaitingTimeNormalizationValue = 1;
                                            ReadCMDWaitingTimeNormalizationValue = SSDController.HostInterface.AverageReadCMDWaitingTime;
                                            if (ReadCMDWaitingTimeNormalizationValue == 0)
                                                ReadCMDWaitingTimeNormalizationValue = 1;
                                            ProgramCMDWaitingTimeNormalizationValue = SSDController.HostInterface.AverageProgramCMDWaitingTime;
                                            if (ProgramCMDWaitingTimeNormalizationValue == 0)
                                                ProgramCMDWaitingTimeNormalizationValue = 1;
                                        }
                                        verySummerizedResultFile.WriteLine(inputParams.NetParameters.BusChannelCount + "x" + inputParams.NetParameters.ChipCountPerChannel + "\t"
                                            + inputParams.ControllerParameters.PlaneAllocationScheme + "\t"
                                            + NotListedControllerParameterSet.GCProperties.GCPolicy + "\t"
                                            + inputParams.ControllerParameters.SchedulingPolicy + "\t"
                                            + inputParams.FlashChipParameters.dieNoPerChip + "\t"
                                            + inputParams.FlashChipParameters.planeNoPerDie + "\t"
                                        + (inputParams.ControllerParameters.Workloads[0] as TraceBasedParameterSet).FilePath.Remove(0, (inputParams.ControllerParameters.Workloads[0] as TraceBasedParameterSet).FilePath.LastIndexOf("\\") + 1) + "\t"
                                        + (inputParams.ControllerParameters.Workloads[0] as TraceBasedParameterSet).PercentageToBeSimulated + "%\t"
                                        + NotListedMessageGenerationParameterSet.Mode + "\t"

                                        /*This part can be used if we have different scenarios and we want to normalize critical values to the values of first scenario
                                        + (SSDController.HostInterface.AvgResponseTime / RTNomralizationValue) + "\t"
                                        + (SSDController.HostInterface.IOPS / IOPSNormalizationValue) + "\t"
                                        + ((double)SSDController.FTL.IssuedInterleaveReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedMultiplaneReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedInterleaveProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedInterleaveMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                        + (SSDController.FTL.PlanePageReadsStdDev / PlaneReadSTDevNormalizationValue) + "\t"
                                        + (SSDController.FTL.PlanePageProgramsStdDev / PlaneProgramSTDevNormalizationValue) + "\t"
                                        + (SSDController.HostInterface.AverageCMDWaitingTime / CMDWaitingTimeNormalizationValue) + "\t"
                                        + (SSDController.HostInterface.AverageReadCMDWaitingTime / ReadCMDWaitingTimeNormalizationValue) + "\t"
                                        + (SSDController.HostInterface.AverageProgramCMDWaitingTime / ProgramCMDWaitingTimeNormalizationValue) + "\t"*/

                                        + SSDController.HostInterface.AvgResponseTime.ToString() + "\t"
                                        + SSDController.HostInterface.MinResponseTime.ToString() + "\t"
                                        + SSDController.HostInterface.MaxResponseTime.ToString() + "\t"
                                        + SSDController.HostInterface.IOPS + "\t"

                                        + SSDController.FTL.GarbageCollector.TotalGCExecutionCount + "\t"
                                        + SSDController.FTL.GarbageCollector.WriteAmplification + "\t" + SSDController.FTL.GarbageCollector.AverageGCCost + "\t"
                                        + SSDController.FTL.GarbageCollector.WearLevelingFairness + "\t" + SSDController.FTL.GarbageCollector.AgingRate + "\t"
                                        + SSDController.FTL.GarbageCollector.BlockEraseCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountStdDev + "\t"
                                        + SSDController.FTL.GarbageCollector.BlockEraseCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountMin + "\t"

                                        + (SSDController.FTL.IssuedReadCMD).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedInterleaveReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedMultiplaneReadCMD / (double)(SSDController.FTL.IssuedReadCMD == 0 ? 1 : SSDController.FTL.IssuedReadCMD) * 100).ToString() + "\t"
                                        + (SSDController.FTL.IssuedProgramCMD).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedInterleaveProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                        + ((double)SSDController.FTL.IssuedInterleaveMultiplaneProgramCMD / (double)(SSDController.FTL.IssuedProgramCMD == 0 ? 1 : SSDController.FTL.IssuedProgramCMD) * 100).ToString() + "\t"
                                        + (SSDController.FTL.IssuedEraseCMD).ToString() + "\t"
                                        + (SSDController.FTL.IssuedInterleaveEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"
                                        + (SSDController.FTL.IssuedMultiplaneEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"
                                        + (SSDController.FTL.IssuedInterleaveMultiplaneEraseCMD / (double)(SSDController.FTL.IssuedEraseCMD == 0 ? 1 : SSDController.FTL.IssuedEraseCMD) * 100).ToString() + "\t"

                                        + SSDController.FTL.AveragePageReadsPerPlane + "\t" + SSDController.FTL.PlanePageReadsStdDev + "\t" + SSDController.FTL.MaxPlaneReadCount + "\t" + SSDController.FTL.MinPlaneReadCount + "\t"
                                        + SSDController.FTL.AveragePageProgramsPerPlane + "\t" + SSDController.FTL.PlanePageProgramsStdDev + "\t" + SSDController.FTL.MaxPlaneProgramCount + "\t" + SSDController.FTL.MinPlaneProgramCount + "\t"
                                        + SSDController.FTL.AverageBlockErasesPerPlane + "\t" + SSDController.FTL.PlaneBlockErasesStdDev + "\t" + SSDController.FTL.MaxPlaneEraseCount + "\t" + SSDController.FTL.MinPlaneEraseCount + "\t"
                                        + SSDController.FTL.AverageNumberOfFreePagesPerPlane + "\t" + SSDController.FTL.PlaneFreePagesStdDev + "\t" + SSDController.FTL.MaxPlaneFreePagesCount + "\t" + SSDController.FTL.MinPlaneFreePagesCount + "\t"
                                        + SSDController.FTL.AverageNumberOfValidPagesPerPlane + "\t" + SSDController.FTL.PlaneValidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneValidPagesCount + "\t" + SSDController.FTL.MinPlaneValidPagesCount + "\t"
                                        + SSDController.FTL.AverageNumberOfInvalidPagesPerPlane + "\t" + SSDController.FTL.PlaneInvalidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneInvalidPagesCount + "\t" + SSDController.FTL.MinPlaneInvalidPagesCount);
                                    }
                                    #endregion
                                    break;
                            }
                            SSDController.HostInterface = null;
                            SSDController = null;
                            XEngineFactory.XEngine.Reset();
                            GC.Collect();
                        }
                    }
                }
            }
        }
        private void MultiParameterExecutionNVMe(SSDParameterSet inputParams, SSDParameterSet inputParamsNext)
        {
            summerizedResultFile.WriteLine("Topology\tAllocationScheme\tIOSchedulingPolicy"
            + "\tFlowDefinition\tRatioOfIgnoredRequests"
            + "\tHandledReqs\tHandledReadReqs\tHandledWriteReqs"
            + "\tResponseTime(us)\tMinResponseTime(us)\tMaxResponseTime(us)"
            + "\tReadResponseTime(us)\tMinReadResponseTime(us)\tMaxReadResponseTime(us)"
            + "\tWriteResponseTime(us)\tMinWriteResponseTime(us)\tMaxWriteResponseTime(us)"
            + "\tIOPS\tIOPSReads\tIOPSWrites\tBandWidth(MB/S)"

            + "\tWriteAmplification\tAverageGCCost\tWearLeveling\tAgingRate\tAverageBlockEraseCount\tBlockEraseStdDev\tMaxBlockEraseCount\tMinBlockEraseCount"
            + "\tBlockValidPagesCountAverage\tBlockValidPagesCountStdDev\tBlockValidPagesCountMax\tBlockValidPagesCountMin"
            + "\tBlockInvalidPagesCountAverage\tBlockInvalidPagesCountStdDev\tBlockInvalidPagesCountMax\tBlockInvalidPagesCountMin"
            + "\tAverageNoOfComparisonsToFindCandidateBlock\tAverageNoOfRandomNumberGenerationToCreateRandomSet"
            + "\tEGCExecutionCount\tAverageEGCCost\tAverageEGCTriggerInterval"

            + "\tTotalIssuedReadCMD\tInterleavedReadPercentage\tMultiplaneReadPercentage"
            + "\tTotalIssuedProgramCMD\tInterleavedProgramPercentage\tMultiplaneProgramPercentage\tInterleavedMultiplaneProgramPercentage"
            + "\tTotalIssuedEraseCMD\tInterleavedErasePercentage\tMultiplaneErasePercentage\tInterleavedMultiplaneErasePercentage"
            + "\tAverageCMDLifeTime(us)\tAverageCMDTransferTime(us)\tAverageCMDWaitingTime(us)"
            + "\tAverageReadCMDLifeTime(us)\tAverageReadCMDTransferTime(us)\tAverageReadCMDWaitingTime(us)"
            + "\tAverageProgramCMDLifeTime(us)\tAverageProgramCMDTransferTime(us)\tAverageProgramCMDWaitingTime(us)"
            + "\tChip_TotalExecutionPeriodNet\tChip_TotalExecutionOverlapped\tChip_TotalTransferPeriodNet\tChip_TotalIdlePeriod"

            + "\tAveragePlaneReadCount\tPlaneReadStdDev\tMaxPlaneReadCount\tMinPlaneReadCount"
            + "\tAveragePlaneProgramCount\tPlaneProgramStdDev\tMaxPlaneProgramCount\tMinPlaneProgramCount"
            + "\tAveragePlaneEraseCount\tPlaneEraseStdDev\tMaxPlaneEraseCount\tMinPlaneEraseCount"
            + "\tAveragePlaneFreePagesCount\tPlaneFreePagesStdDev\tMaxPlaneFreePagesCount\tMinPlaneFreePagesCount"
            + "\tAveragePlaneValidPagesCount\tPlaneValidPagesStdDev\tMaxPlaneValidPagesCount\tMinPlaneValidPagesCount"
            + "\tAveragePlaneInvalidPagesCount\tPlaneInvalidPagesStdDev\tMaxPlaneInvalidPagesCount\tMinPlaneInvalidPagesCount");


            int syntheticStreamID = -1;

            for (int i = 0; i < inputParams.ControllerParameters.Workloads.Length; i++)
                if (inputParams.ControllerParameters.Workloads[i] is SyntheticParameterSet)
                    syntheticStreamID = i;

            if (syntheticStreamID > -1)
            {
                uint[] reqInterArrival;
                uint reqInterArrivalNo = 4;
                uint initInterArrivalVal = 100000;
                if (investigationList.ContainsValue(ParameterType.RequestInterArrival))
                {
                    reqInterArrival = new uint[reqInterArrivalNo];
                    for (uint i = 0; i < reqInterArrivalNo; i++)
                    {
                        reqInterArrival[i] = initInterArrivalVal;
                        initInterArrivalVal *= 10;
                    }
                }
                else
                {
                    reqInterArrivalNo = 1;
                    reqInterArrival = new uint[1];
                    reqInterArrival[0] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).AverageRequestInterArrivalTime;
                }

                uint requestSizeNo = 10;
                uint[] normalReqSizeMean = { 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 };
                uint[] normalReqSizeVariance = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 };
                if (!investigationList.ContainsValue(ParameterType.RequestSize))
                {
                    requestSizeNo = 1;
                    normalReqSizeMean = new uint[1];
                    normalReqSizeMean[0] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistParam1;
                    normalReqSizeVariance = new uint[1];
                    normalReqSizeVariance[0] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistParam2;
                }

                uint readRatioNo = 5;
                double[] readRatio = { 1.0, 0.75, 0.5, 0.25, 0.0 };
                if (!investigationList.ContainsValue(ParameterType.RequestType))
                {
                    readRatioNo = 1;
                    readRatio = new double[1];
                    readRatio[0] = (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).ReadRatio;
                }
            }
           // for (uint reqInterArrivalCntr = 0; reqInterArrivalCntr < reqInterArrivalNo; reqInterArrivalCntr++)
            {
               // (inputParams.ControllerParameters.Workloads[syntheticStreamID] as SyntheticParameterSet).AverageRequestInterArrivalTime = reqInterArrival[reqInterArrivalCntr];
                //for (uint reqSizeCounter = 0; reqSizeCounter < requestSizeNo; reqSizeCounter++)
                {
                   // (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistParam1 = normalReqSizeMean[reqSizeCounter];
                  //  (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).reqSizeDistParam2 = normalReqSizeVariance[reqSizeCounter];
                   // for (uint readRatioCntr = 0; readRatioCntr < readRatioNo; readRatioCntr++)
                    {
                    //    (inputParams.ControllerParameters.Workloads[0] as SyntheticParameterSet).ReadRatio = readRatio[readRatioCntr];
                        IORequest.lastId = 0;
                        InputStreamBase.ResetGlobalID();
                        Simulate(inputParams, inputParamsNext);
                        for (uint streamCntr = 0; streamCntr < inputParams.ControllerParameters.Workloads.Length; streamCntr++)
                        {
                            #region WriteToSummarizedResultFile
                            summerizedResultFile.Write(inputParams.NetParameters.BusChannelCount + "x" + inputParams.NetParameters.ChipCountPerChannel + "[d="
                                + inputParams.FlashChipParameters.dieNoPerChip + ",p="
                                + inputParams.FlashChipParameters.planeNoPerDie + "]\t"
                                + inputParams.ControllerParameters.PlaneAllocationScheme + "," + inputParams.ControllerParameters.BlockAllocationScheme + "\t"
                                + inputParams.ControllerParameters.SchedulingPolicy + "\t");
                            if ((SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr] is InputStreamTraceBased)
                                summerizedResultFile.Write(((SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr] as InputStreamTraceBased)._traceFilePath.Remove(0, ((SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr] as InputStreamTraceBased)._traceFilePath.LastIndexOf("\\") + 1) + ",Rep="
                            + ((SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr] as InputStreamTraceBased).TotalReplayCount + "\t");
                            else
                                summerizedResultFile.Write("Synth,Rate["
                            + ((double)(inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).AverageRequestInterArrivalTime / 1000000).ToString("F2") + "ms],Size["
                            + (inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).reqSizeDistType + ","
                            + ((double)(inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).reqSizeDistParam1 / 2).ToString("F1") + "],ADR["
                            + (inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).AddressDistType + ","
                            + ((inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).AddressDistType == InputStreamSynthetic.DistributionType.HotCold ?
                                "f=" + ((double)(inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).AddressDistParam1)
                                + ",r=" + ((double)(inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).AddressDistParam2)
                                :
                                ((double)(inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).AddressDistParam1).ToString("P0") + "-"
                                + ((double)(inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).AddressDistParam2).ToString("P0")
                            ) + "],Read["
                            + ((int)((inputParams.ControllerParameters.Workloads[streamCntr] as SyntheticParameterSet).ReadRatio * 100)) + "%]\t");

                            summerizedResultFile.WriteLine((SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].RatioOfIgnoredRequests + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].HandledRequestsCount + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].HandledReadRequestsCount + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].HandledWriteRequestsCount + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AvgResponseTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].MinResponseTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].MaxResponseTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AvgResponseTimeR.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].MinResponseTimeR.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].MaxResponseTimeR.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AvgResponseTimeW.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].MinResponseTimeW.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].MaxResponseTimeW.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].IOPS + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].IOPSReads + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].IOPSWrites + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AggregateBandWidth.ToString() + "\t"

                            + SSDController.FTL.GarbageCollector.WriteAmplification + "\t" + SSDController.FTL.GarbageCollector.AverageGCCost + "\t"
                            + SSDController.FTL.GarbageCollector.WearLevelingFairness + "\t" + SSDController.FTL.GarbageCollector.AgingRate + "\t"
                            + SSDController.FTL.GarbageCollector.BlockEraseCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountStdDev + "\t"
                            + SSDController.FTL.GarbageCollector.BlockEraseCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockEraseCountMin + "\t"
                            + SSDController.FTL.GarbageCollector.BlockValidPagesCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockValidPagesCountStdDev + "\t"
                            + SSDController.FTL.GarbageCollector.BlockValidPagesCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockValidPagesCountMin + "\t"
                            + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountAverage + "\t" + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountStdDev + "\t"
                            + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountMax + "\t" + SSDController.FTL.GarbageCollector.BlockInvalidPagesCountMin + "\t"
                            + SSDController.FTL.GarbageCollector.AverageNoOfComparisonsToFindCandidateBlock + "\t" + SSDController.FTL.GarbageCollector.AverageNoOfRandomNumberGenerationToCreateRandomSet + "\t"

                            + (SSDController.FTL.GarbageCollector.EmergencyGCExecutionCount).ToString() + "\t"
                            + (SSDController.FTL.GarbageCollector.AverageEmergencyGCCost).ToString() + "\t"
                            + (SSDController.FTL.GarbageCollector.AverageEmergencyGCTriggerInterval).ToString() + "\t"

                            + (SSDController.FTL.IssuedReadCMD).ToString() + "\t"
                            + ((double)SSDController.FTL.IssuedInterleaveReadCMD / (double)SSDController.FTL.IssuedReadCMD * 100).ToString() + "\t"
                            + ((double)SSDController.FTL.IssuedMultiplaneReadCMD / (double)SSDController.FTL.IssuedReadCMD * 100).ToString() + "\t"
                            + (SSDController.FTL.IssuedProgramCMD).ToString() + "\t"
                            + ((double)SSDController.FTL.IssuedInterleaveProgramCMD / (double)SSDController.FTL.IssuedProgramCMD * 100).ToString() + "\t"
                            + ((double)SSDController.FTL.IssuedMultiplaneProgramCMD / (double)SSDController.FTL.IssuedProgramCMD * 100).ToString() + "\t"
                            + ((double)SSDController.FTL.IssuedInterleaveMultiplaneProgramCMD / (double)SSDController.FTL.IssuedProgramCMD * 100).ToString() + "\t"
                            + (SSDController.FTL.IssuedEraseCMD).ToString() + "\t"
                            + (SSDController.FTL.IssuedInterleaveEraseCMD / (double)SSDController.FTL.IssuedEraseCMD * 100).ToString() + "\t"
                            + (SSDController.FTL.IssuedMultiplaneEraseCMD / (double)SSDController.FTL.IssuedEraseCMD * 100).ToString() + "\t"
                            + (SSDController.FTL.IssuedInterleaveMultiplaneEraseCMD / (double)SSDController.FTL.IssuedEraseCMD * 100).ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageCMDLifeTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageCMDTransferTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageCMDWaitingTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageReadCMDLifeTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageReadCMDTransferTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageReadCMDWaitingTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageProgramCMDLifeTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageProgramCMDTransferTime.ToString() + "\t"
                            + (SSDController.HostInterface as HostInterfaceNVMe).InputStreams[streamCntr].AverageProgramCMDWaitingTime.ToString() + "\t"
                            + SSDController.FTL.AverageFlashChipCMDExecutionPeriodNet.ToString() + "\t"
                            + SSDController.FTL.AverageFlashChipCMDExecutionPeriodOverlapped.ToString() + "\t"
                            + SSDController.FTL.AverageFlashChipTransferPeriodNet.ToString() + "\t"
                            + SSDController.FTL.AverageFlashChipeIdePeriod.ToString() + "\t"

                            + SSDController.FTL.AveragePageReadsPerPlane + "\t" + SSDController.FTL.PlanePageReadsStdDev + "\t" + SSDController.FTL.MaxPlaneReadCount + "\t" + SSDController.FTL.MinPlaneReadCount + "\t"
                            + SSDController.FTL.AveragePageProgramsPerPlane + "\t" + SSDController.FTL.PlanePageProgramsStdDev + "\t" + SSDController.FTL.MaxPlaneProgramCount + "\t" + SSDController.FTL.MinPlaneProgramCount + "\t"
                            + SSDController.FTL.AverageBlockErasesPerPlane + "\t" + SSDController.FTL.PlaneBlockErasesStdDev + "\t" + SSDController.FTL.MaxPlaneEraseCount + "\t" + SSDController.FTL.MinPlaneEraseCount + "\t"
                            + SSDController.FTL.AverageNumberOfFreePagesPerPlane + "\t" + SSDController.FTL.PlaneFreePagesStdDev + "\t" + SSDController.FTL.MaxPlaneFreePagesCount + "\t" + SSDController.FTL.MinPlaneFreePagesCount + "\t"
                            + SSDController.FTL.AverageNumberOfValidPagesPerPlane + "\t" + SSDController.FTL.PlaneValidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneValidPagesCount + "\t" + SSDController.FTL.MinPlaneValidPagesCount + "\t"
                            + SSDController.FTL.AverageNumberOfInvalidPagesPerPlane + "\t" + SSDController.FTL.PlaneInvalidPagesStdDev + "\t" + SSDController.FTL.MaxPlaneInvalidPagesCount + "\t" + SSDController.FTL.MinPlaneInvalidPagesCount);
                            #endregion
                        }
                        summerizedResultFile.WriteLine("");
                    }
                }
            }
            SSDController.HostInterface = null;
            SSDController = null;
            XEngineFactory.XEngine.Reset();
            GC.Collect();
        }
        public void Simulate1(ExecutionParameterSet iparam, ExecutionParameterSet iparamNext)
        {
            SSDParameterSet parameters = (iparam as SSDParameterSet);

            /*string outputNames = (parameters.controllerParameters.WorkloadProperties.Type == HostInterface.RequestGeneratorType.TraceBased ?
                        parameters.controllerParameters.WorkloadProperties.filePath.Substring(0, parameters.controllerParameters.WorkloadProperties.filePath.LastIndexOf(".trace"))
                        : parameters.controllerParameters.WorkloadProperties.filePath.Substring(0, parameters.controllerParameters.WorkloadProperties.filePath.LastIndexOf("\\")) + "\\Synthetic")
                + "." + (parameters.controllerParameters.WorkloadProperties.Mode == HostInterface.ReqeustGenerationMode.Normal ? "norm" : "sat")
                + "." + parameters.netParameters.chipsCommunication + parameters.netParameters.k1 + "x" + parameters.netParameters.k2
                + "." + "(d" + parameters.flashChipParameters.dieNoPerChip + "p" + parameters.flashChipParameters.planeNoPerDie + ")"
                + "." + "gc=" + parameters.controllerParameters.GCProperties.GCPolicy
                + (parameters.controllerParameters.GCProperties.GCPolicy == GarbageCollector.GCPolicyType.RGA? "(d=" + parameters.controllerParameters.GCProperties.RGAConstant + ")" : "")
                + (parameters.controllerParameters.GCProperties.GCPolicy == GarbageCollector.GCPolicyType.WindowedGreedy?"(w=" + parameters.controllerParameters.GCProperties.WGreedyWindowSize +")" : "")
                + "." + "W=" + (100 - parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.ReadPercentage) + "%"
                + "." + "size=" + (parameters.controllerParameters.WorkloadProperties.SyntheticGenerationProperties.reqSizeDistParam1 / 1024) + "KB"
                + "." + "cw=" + parameters.netParameters.channelWidth
                + "." + "freq=" + (1000 / parameters.netParameters.readTransferCycleTime) + "MHz"
                + (parameters.netParameters.chipsCommunication != DiskTopology.Bus ? ".inj=" + parameters.controllerParameters.injectionPolicy + ".pck=" + parameters.netParameters.packetSize + ".buf=" + parameters.netParameters.chipBufferDepth : "")
                ;// +"." + "aloc=" + parameters.controllerParameters.allocationScheme;*/

            string outputNames = runPath;
            outputNames = outputNames.Remove(outputNames.LastIndexOf("."));

            summerizedResultFile = new System.IO.StreamWriter(outputNames + "-result.res");
            if (writeToVerySummerizedFile)
                verySummerizedResultFile = new System.IO.StreamWriter(outputNames + "-sum-result.res");

            switch (parameters.ControllerParameters.HostInterfaceType)
            {
                case HostInterface.HostInterfaceType.SATATraceBased:
                    MultiParameterExecutionTraceBasedSingleFlow(iparam as SSDParameterSet, iparamNext as SSDParameterSet);
                    break;
                case HostInterface.HostInterfaceType.NVMe:
                    MultiParameterExecutionNVMe(iparam as SSDParameterSet, iparamNext as SSDParameterSet);
                    break;
                case HostInterface.HostInterfaceType.SATASynthetic://Synthetic message generation
                    MultiParameterExecutionSyntheticSATA(iparam as SSDParameterSet, iparamNext as SSDParameterSet);
                    break;
            }
            summerizedResultFile.Close();
            if (writeToVerySummerizedFile)
                verySummerizedResultFile.Close();
        }

        [STAThread]
        static void Main(string[] args)
        {
            SSDParameterSet[] param = new SSDParameterSet[] { new SSDParameterSet() };
            SSD network = new SSD("SSD", null);
            network.investigationList = new Hashtable();
            if (args.Length > 1)
            {
                for (int i = 1; i < args.Length; i++)
                {
                    switch (args[i].ToLower())
                    {
                        case "t":
                        case "topo":
                        case "topology":
                            network.investigationList.Add(ParameterType.Topology, ParameterType.Topology);
                            break;
                        case "as":
                        case "mapping":
                        case "mappingpolicy":
                        case "allocastionscheme":
                        case "allocscheme":
                            network.investigationList.Add(ParameterType.AllocationSchemes, ParameterType.AllocationSchemes);
                            break;
                        case "d":
                        case "die":
                        case "dieno":
                            network.investigationList.Add(ParameterType.DieNo, ParameterType.DieNo);
                            break;
                        case "p":
                        case "plan":
                        case "plane":
                        case "planeno":
                            network.investigationList.Add(ParameterType.PlaneNo, ParameterType.PlaneNo);
                            break;
                        case "rate":
                        case "ar":
                        case "arrival":
                        case "arrivalrate":
                        case "arrivaltime":
                            network.investigationList.Add(ParameterType.RequestInterArrival, ParameterType.RequestInterArrival);
                            break;
                        case "rs":
                        case "requestsize":
                        case "reqsize":
                        case "avgreqsize":
                            network.investigationList.Add(ParameterType.RequestSize, ParameterType.RequestSize);
                            break;
                        case "ad":
                        case "rad":
                        case "requestaddress":
                        case "addressdistribution":
                            network.investigationList.Add(ParameterType.RequestAddress, ParameterType.RequestAddress);
                            break;
                        case "rt":
                        case "rr":
                        case "wr":
                        case "readratio":
                        case "writeratio":
                            network.investigationList.Add(ParameterType.RequestType, ParameterType.RequestType);
                            break;
                    }
                }
            }
            if (args.Length > 0)
                network.runPath = args[0];
            ParameteSetBasedExecution.Execute(args, param, network.runPath, network);
        }

        private void onStatisticsReady(object sender)
        {
            Console.WriteLine("####################################################");
            Console.WriteLine("Simulation progress is {0:F2}%:", (SSDController.HostInterface as HostInterfaceNVMe).SimulationProgress * 100);
            foreach (InputStreamBase IS in (SSDController.HostInterface as HostInterfaceNVMe).InputStreams)
                Console.WriteLine("\r\nInput Stream: {0}"
                //+ "\nCurrent Replay Round: {1}"
                + "\nCurrent Simulation Time: {1}(us)"
                //+ "\nCompleted requests:{3:p}"
                + "\nAverageRT={2}(us), AverageIOPS={3:F2}, AverageBandwidth={4:F2}(MB/s)",
                IS.FlowName,
                //IS.CurrentReplayRound,
                XEngineFactory.XEngine.Time / 1000,
                IS.AvgResponseTime, IS.IOPS, IS.AggregateBandWidth);
            Console.WriteLine("####################################################");
        }
    }
}
