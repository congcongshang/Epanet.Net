/*
 * Copyright (C) 2016 Vyacheslav Shevelyov (slavash at aha dot ru)
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see http://www.gnu.org/licenses/.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using org.addition.epanet.hydraulic.io;
using org.addition.epanet.hydraulic.structures;
using org.addition.epanet.log;
using org.addition.epanet.network;
using org.addition.epanet.network.structures;
using org.addition.epanet.quality.structures;
using org.addition.epanet.util;

namespace org.addition.epanet.quality {

///<summary>Single species water quality simulation class.</summary>
public class QualitySim {
    ///<summary>Bulk reaction units conversion factor.</summary>
    private double Bucf;
    private readonly FieldsMap fMap;
    ///<summary>Current hydraulic time counter [seconds].</summary>
    private long Htime;

    private readonly List<QualityNode> juncs;
    private readonly List<QualityLink> links;
    private readonly Network net;
    private readonly List<QualityNode> nodes;
    ///<summary>Number of reported periods.</summary>
    private int Nperiods;

    private readonly PropertiesMap pMap;

    ///<summary>Current quality time (sec)</summary>
    private long Qtime;

    ///<summary>Reaction indicator.</summary>
    private bool Reactflag;
    ///<summary>Current report time counter [seconds].</summary>
    private long Rtime;
    ///<summary>Schmidt Number.</summary>
    private double Sc;
    private readonly List<QualityTank> tanks;
    private QualityNode traceNode;
    ///<summary>Tank reaction units conversion factor.</summary>
    private double Tucf;
    ///<summary>Avg. bulk reaction rate.</summary>
    private double Wbulk;
    ///<summary>Avg. mass inflow.</summary>
    private double Wsource;
    ///<summary>Avg. tank reaction rate.</summary>
    private double Wtank;
    ///<summary>Avg. wall reaction rate.</summary>
    private double Wwall;

    [NonSerialized]
    private readonly double elevUnits;
    [NonSerialized]
    private readonly PropertiesMap.QualType qualflag;


    ///<summary>Initializes WQ solver system</summary>

    public QualitySim(Network net, TraceSource ignored) {
//        this.log = log;
        this.net = net;
        this.fMap = net.getFieldsMap();
        this.pMap = net.getPropertiesMap();

        nodes = new List<QualityNode>();
        links = new List<QualityLink>();
        tanks = new List<QualityTank>();
        juncs = new List<QualityNode>();

        List<Node> nNodes = new List<Node>(net.getNodes());

        foreach (Node n  in  nNodes) {
            QualityNode qN = QualityNode.create(n);

            nodes.Add(qN);

            if (qN is QualityTank)
                tanks.Add((QualityTank) qN);
            else
                juncs.Add(qN);
        }

        foreach (Link n  in  net.getLinks())
            links.Add(new QualityLink(nNodes, nodes, n));

        Bucf = 1.0;
        Tucf = 1.0;
        Reactflag = false;

        qualflag = pMap.getQualflag();
        if (qualflag != PropertiesMap.QualType.NONE) {
            if (qualflag == PropertiesMap.QualType.TRACE) {
                foreach (QualityNode qN  in  nodes)
                    if (qN.getNode().getId().Equals(pMap.getTraceNode(), StringComparison.OrdinalIgnoreCase)) {
                        traceNode = qN;
                        traceNode.setQuality(100.0);
                        break;
                    }
            }

            if (pMap.getDiffus() > 0.0)
                Sc = pMap.getViscos() / pMap.getDiffus();
            else
                Sc = 0.0;

            Bucf = getUcf(pMap.getBulkOrder());
            Tucf = getUcf(pMap.getTankOrder());

            Reactflag = getReactflag();
        }


        Wbulk = 0.0;
        Wwall = 0.0;
        Wtank = 0.0;
        Wsource = 0.0;

        Htime = 0;
        Rtime = pMap.getRstart();
        Qtime = 0;
        Nperiods = 0;
        elevUnits = fMap.getUnits(FieldsMap.Type.ELEV);
    }

    /**
     * Accumulates mass flow at nodes and updates nodal quality.
     *
     * @param dt step duration in seconds.
     */
    private void accumulate(long dt) {
        //  Re-set memory used to accumulate mass & volume
        foreach (QualityNode qN  in  nodes) {
            qN.setVolumeIn(0);
            qN.setMassIn(0);
            qN.setSourceContribution(0);
        }

        foreach (QualityLink qL  in  links) {
            QualityNode j = qL.getDownStreamNode();   //  Downstream node
            if (qL.getSegments().Count > 0)   //  Accumulate concentrations
            {
                j.setMassIn(j.getMassIn() + qL.getSegments().First.Value.c);
                j.setVolumeIn(j.getVolumeIn() + 1);
            }
            j = qL.getUpStreamNode();
            if (qL.getSegments().Count > 0)  // Upstream node
            {                               // Accumulate concentrations
                j.setMassIn(j.getMassIn() + qL.getSegments().Last.Value.c);
                j.setVolumeIn(j.getVolumeIn() + 1);
            }
        }

        foreach (QualityNode qN  in  nodes)
            if (qN.getVolumeIn() > 0.0)
                qN.setSourceContribution(qN.getMassIn() / qN.getVolumeIn());

        //  Move mass from first segment of each pipe into downstream node
        foreach (QualityNode qN  in  nodes) {
            qN.setVolumeIn(0);
            qN.setMassIn(0);
        }

        foreach (QualityLink qL  in  links) {
            QualityNode j = qL.getDownStreamNode();
            double v = Math.Abs(qL.getFlow()) * dt;


            while (v > 0.0) {
                if (qL.getSegments().Count == 0)
                    break;

                QualitySegment seg = qL.getSegments().First.Value;

                // Volume transported from this segment is
                // minimum of flow volume & segment volume
                // (unless leading segment is also last segment)
                double vseg = seg.v;
                vseg = Math.Min(vseg, v);

                if (qL.getSegments().Count == 1)
                    vseg = v;

                double cseg = seg.c;
                j.setVolumeIn(j.getVolumeIn() + vseg);
                j.setMassIn(j.getMassIn() + vseg * cseg);

                v -= vseg;

                // If all of segment's volume was transferred, then
                // replace leading segment with the one behind it
                // (Note that the current seg is recycled for later use.)
                if (v >= 0.0 && vseg >= seg.v) {
                    qL.getSegments().RemoveFirst(); 
                } else {
                    seg.v -= vseg;
                }
            }
        }
    }

    ///<summary>Computes average quality in link.</summary>
    double avgqual(QualityLink ql) {
        double vsum = 0.0,
                msum = 0.0;

        if (qualflag == PropertiesMap.QualType.NONE)
            return (0.0);

        foreach (QualitySegment seg  in  ql.getSegments()) {
            vsum += seg.v;
            msum += (seg.c) * (seg.v);
        }

        if (vsum > 0.0)
            return (msum / vsum);
        else
            return ((ql.getFirst().getQuality() + ql.getSecond().getQuality()) / 2.0);
    }

    ///<summary>Computes bulk reaction rate (mass/volume/time).</summary>
    private double bulkrate(double c, double kb, double order) {
        double c1;

        if (order == 0.0)
            c = 1.0;
        else if (order < 0.0) {
            c1 = pMap.getClimit() + Utilities.getSignal(kb) * c;
            if (Math.Abs(c1) < Constants.TINY) c1 = Utilities.getSignal(c1) * Constants.TINY;
            c = c / c1;
        } else {
            if (pMap.getClimit() == 0.0)
                c1 = c;
            else
                c1 = Math.Max(0.0, Utilities.getSignal(kb) * (pMap.getClimit() - c));

            if (order == 1.0)
                c = c1;
            else if (order == 2.0)
                c = c1 * c;
            else
                c = c1 * Math.Pow(Math.Max(0.0, c), order - 1.0);
        }


        if (c < 0) c = 0;
        return (kb * c);
    }


    ///<summary>Retrieves hydraulic solution and hydraulic time step for next hydraulic event.</summary>
    private void gethyd(BinaryWriter outStream, HydraulicReader hydSeek) {
        AwareStep step =  hydSeek.getStep((int) Htime);
        loadHydValues(step);

        Htime += step.getStep();

        if (Htime >= Rtime) {
            saveOutput(outStream);
            Nperiods++;
            Rtime += pMap.getRstep();
        }


        if (qualflag != PropertiesMap.QualType.NONE && Qtime < pMap.getDuration()) {
            if (Reactflag && qualflag != PropertiesMap.QualType.AGE)
                ratecoeffs();

            if (Qtime == 0)
                initsegs();
            else
                reorientsegs();
        }
    }


    public long getQtime() {
        return Qtime;
    }

    ///<summary>Checks if reactive chemical being simulated.</summary>
    private bool getReactflag() {
        if (qualflag == PropertiesMap.QualType.TRACE)
            return (false);
        else if (qualflag == PropertiesMap.QualType.AGE)
            return (true);
        else {
            foreach (QualityLink qL  in  links) {
                if (qL.getLink().getType() <= Link.LinkType.PIPE) {
                    if (qL.getLink().getKb() != 0.0 || qL.getLink().getKw() != 0.0)
                        return (true);
                }
            }
            foreach (QualityTank qT  in  tanks)
                if (((Tank) qT.getNode()).getKb() != 0.0)
                    return (true);
        }
        return (false);
    }

    ///<summary>Local method to compute unit conversion factor for bulk reaction rates.</summary>
    private double getUcf(double order) {
        if (order < 0.0)
            order = 0.0;

        if (order == 1.0)
            return (1.0);
        else
            return (1.0 / Math.Pow(Constants.LperFT3, (order - 1.0)));
    }

    ///<summary>Initializes water quality segments.</summary>
    private void initsegs() {
        foreach (QualityLink qL  in  links) {
            qL.setFlowDir(true);

            if (qL.getFlow() < 0.0)
                qL.setFlowDir(false);

            qL.getSegments().Clear();

            double c;

            // Find quality of downstream node
            QualityNode j = qL.getDownStreamNode();
            if (!(j is QualityTank))
                c = j.getQuality();
            else
                c = ((QualityTank) j).getConcentration();

            // Fill link with single segment with this quality
            qL.getSegments().AddLast(new QualitySegment(qL.getLinkVolume(), c));
        }

        // Initialize segments in tanks that use them
        foreach (QualityTank qT  in  tanks) {

            // Skip reservoirs & complete mix tanks
            if (((Tank) qT.getNode()).getArea() == 0.0 ||
                    ((Tank) qT.getNode()).getMixModel() == Tank.MixType.MIX1)
                continue;

            double c = qT.getConcentration();

            qT.getSegments().Clear();

            // Add 2 segments for 2-compartment model
            if (((Tank) qT.getNode()).getMixModel() == Tank.MixType.MIX2) {
                double v = Math.Max(0, qT.getVolume() - ((Tank) qT.getNode()).getV1max());
                qT.getSegments().AddLast(new QualitySegment(v, c));
                v = qT.getVolume() - v;
                qT.getSegments().AddLast(new QualitySegment(v, c));
            } else {
                // Add one segment for FIFO & LIFO models
                double v = qT.getVolume();
                qT.getSegments().AddLast(new QualitySegment(v, c));
            }
        }
    }



    ///<summary>Load hydraulic simulation data to the water quality structures.</summary>
    private void loadHydValues(AwareStep step) {
        int count = 0;
        foreach (QualityNode qN  in  nodes) {
            qN.setDemand(step.getNodeDemand(count++,qN.getNode(), null));
        }

        count = 0;
        foreach (QualityLink qL  in  links) {
            qL.setFlow(step.getLinkFlow(count++, qL.getLink(), null));
        }
    }

    ///<summary>Updates WQ conditions until next hydraulic solution occurs (after tstep secs.)</summary>
    private long nextqual(BinaryWriter outStream) {
        long hydstep = this.Htime - this.Qtime;

        if (qualflag != PropertiesMap.QualType.NONE && hydstep > 0)
            transport(hydstep);

        long tstep = hydstep;
        Qtime += hydstep;

        if (tstep == 0)
            saveFinaloutput(outStream);

        return (tstep);
    }

    private long nextqual(){
        long hydstep;
        long tstep;

        hydstep = Htime - Qtime;

        if (qualflag != PropertiesMap.QualType.NONE && hydstep > 0)
            transport(hydstep);

        tstep = hydstep;
        Qtime += hydstep;

        return (tstep);
    }

    ///<summary>Finds wall reaction rate coeffs.</summary>
    private double piperate(QualityLink ql) {
        double a, d, u, kf, kw, y, Re, Sh;

        d = ql.getLink().getDiameter();

        if (Sc == 0.0) {
            if (pMap.getWallOrder() == 0.0)
                return (Constants.BIG);
            else
                return (ql.getLink().getKw() * (4.0 / d) / elevUnits);
        }

        a = Constants.PI * d * d / 4.0;
        u = Math.Abs(ql.getFlow()) / a;
        Re = u * d / pMap.getViscos();

        if (Re < 1.0)
            Sh = 2.0;

        else if (Re >= 2300.0)
            Sh = 0.0149 * Math.Pow(Re, 0.88) * Math.Pow(Sc, 0.333);
        else {
            y = d / ql.getLink().getLenght() * Re * Sc;
            Sh = 3.65 + 0.0668 * y / (1.0 + 0.04 * Math.Pow(y, 0.667));
        }


        kf = Sh * pMap.getDiffus() / d;


        if (pMap.getWallOrder() == 0.0) return (kf);


        kw = ql.getLink().getKw() / elevUnits;
        kw = (4.0 / d) * kw * kf / (kf + Math.Abs(kw));
        return (kw);
    }

    ///<summary>Computes new quality in a pipe segment after reaction occurs.</summary>
    private double pipereact(QualityLink ql, double c, double v, long dt) {
        double cnew, dc, dcbulk, dcwall, rbulk, rwall;


        if (qualflag == PropertiesMap.QualType.AGE) return (c + dt / 3600.0);


        rbulk = bulkrate(c, ql.getLink().getKb(), pMap.getBulkOrder()) * Bucf;
        rwall = wallrate(c, ql.getLink().getDiameter(), ql.getLink().getKw(), ql.getFlowResistance());


        dcbulk = rbulk * (double) dt;
        dcwall = rwall * (double) dt;


        if (Htime >= pMap.getRstart()) {
            Wbulk += Math.Abs(dcbulk) * v;
            Wwall += Math.Abs(dcwall) * v;
        }


        dc = dcbulk + dcwall;
        cnew = c + dc;
        cnew = Math.Max(0.0, cnew);
        return (cnew);
    }

    ///<summary>Determines wall reaction coeff. for each pipe.</summary>
    private void ratecoeffs() {
        foreach (QualityLink ql  in  links) {
            double kw = ql.getLink().getKw();
            if (kw != 0.0)
                kw = piperate(ql);

            ql.setFlowResistance(kw);
            //ql.setReactionRate(0.0);
        }
    }

    /**
     * Creates new segments in outflow links from nodes.
     *
     * @param dt step duration in seconds.
     */
    private void release(long dt) {
        foreach (QualityLink qL  in  links) {
            if (qL.getFlow() == 0.0)
                continue;

            // Find flow volume released to link from upstream node
            // (NOTE: Flow volume is allowed to be > link volume.)
            QualityNode qN = qL.getUpStreamNode();
            double q = Math.Abs(qL.getFlow());
            double v = q * dt;

            // Include source contribution in quality released from node.
            double c = qN.getQuality() + qN.getSourceContribution();

            // If link has a last seg, check if its quality
            // differs from that of the flow released from node.
            if (qL.getSegments().Count > 0) {
                QualitySegment seg = qL.getSegments().Last.Value;

                // Quality of seg close to that of node
                if (Math.Abs(seg.c - c) < pMap.getCtol()) {
                    seg.c = (seg.c * seg.v + c * v) / (seg.v + v);
                    seg.v += v;
                } else  // Otherwise add a new seg to end of link
                    qL.getSegments().AddLast(new QualitySegment(v, c));
            } else // If link has no segs then add a new one.
                qL.getSegments().AddLast(new QualitySegment(qL.getLinkVolume(), c));
        }
    }

    ///<summary>Re-orients segments (if flow reverses).</summary>
    private void reorientsegs() {
        foreach (QualityLink qL  in  links) {
            bool newdir = true;

            if (qL.getFlow() == 0.0)
                newdir = qL.getFlowDir();
            else if (qL.getFlow() < 0.0)
                newdir = false;

            if (newdir != qL.getFlowDir()) {
                qL.getSegments().Reverse();
                qL.setFlowDir(newdir);
            }
        }
    }

    /**
     * Write the number of report periods written in the binary output file.
     *
     * @param outStream
     * @throws IOException
     */
    private void saveFinaloutput(BinaryWriter outStream) {
        outStream.Write((int)Nperiods);
    }

    /**
     * Save links and nodes species concentrations for the current step.
     *
     * @throws IOException
     * @throws ENException
     */
    private void saveOutput(BinaryWriter outStream) {

        //System.out.print(Utilities.getClockTime(Rtime)+"\tNodes\t");

        foreach (QualityNode qN  in  nodes) {
            outStream.Write((float) qN.getQuality());
            //if(qN.getNode().isRptFlag())
            //System.out.print( string.format("%.2f\t",fMap.revertUnit(FieldsMap.Type.QUALITY,qN.getQuality())));
        }
        //System.out.print("\n\t\t\tLinks\t");

        foreach (QualityLink qL  in  links) {
            outStream.Write((float) avgqual(qL));
            //if(qL.getLink().isRptFlag())
            //    System.out.print( string.format("%.2f\t",fMap.revertUnit(FieldsMap.Type.QUALITY,avgqual(qL))));
        }
        //System.out.print("\n");

    }

    /**
     * Run the water quality simulation.
     *
     * @param hydFile  The hydraulic output file generated previously.
     * @param qualFile The output file were the water quality simulation results will be written.
     * @throws IOException
     * @throws ENException
     */
    public void simulate(string hydFile, string qualFile) {
        
        using (var bos = File.OpenWrite(qualFile))
            simulate(hydFile, bos);
        
    }

    /**
     * Run the water quality simulation.
     *
     * @param hydFile The hydraulic output file generated previously.
     * @param out     The output stream were the water quality simulation results will be written.
     * @throws IOException
     * @throws ENException
     */
    void simulate(string hydFile, Stream @out) {
        BinaryWriter outStream = new BinaryWriter(@out);
        outStream.Write((int)net.getNodes().Length);
        outStream.Write((int)net.getLinks().Length);
        long tstep;
        HydraulicReader hydraulicReader = new HydraulicReader(new BinaryReader(File.OpenRead(hydFile)));

        do {
            if (Qtime == Htime)
                gethyd(outStream, hydraulicReader);


            tstep = nextqual(outStream);
        } while (tstep > 0);

        hydraulicReader.close();
    }

    /**
     * Simulate water quality during one hydraulic step.
     * @param hydNodes
     * @param hydLinks
     * @return
     * @throws ENException
     */
    public bool simulateSingleStep(List<SimulationNode> hydNodes, List<SimulationLink> hydLinks, long hydStep)
    {
        int count = 0;
        foreach (QualityNode qN  in  nodes) {
            qN.setDemand(hydNodes[count++].getSimDemand());
        }

        count = 0;
        foreach (QualityLink qL  in  links) {
            SimulationLink hL = hydLinks[count++];
            qL.setFlow(hL.getSimStatus() <= Link.StatType.CLOSED ? 0d : hL.getSimFlow());
        }

        Htime += hydStep;

        if (qualflag != PropertiesMap.QualType.NONE && Qtime < pMap.getDuration()){
            if (Reactflag && qualflag != PropertiesMap.QualType.AGE)
                ratecoeffs();

            if (Qtime == 0)
                initsegs();
            else
                reorientsegs();
        }

        long tstep = nextqual();

        if (tstep == 0)
            return false;

        return true;
    }

    /**
     * Computes contribution (if any) of mass additions from WQ sources at each node.
     *
     * @param dt step duration in seconds.
     */
    private void sourceinput(long dt) {
        double qcutoff = 10.0 * Constants.TINY;

        foreach (QualityNode qN  in  nodes)
            qN.setSourceContribution(0);


        if (qualflag != PropertiesMap.QualType.CHEM)
            return;

        foreach (QualityNode qN  in  nodes) {
            Source source = qN.getNode().getSource();

            // Skip node if no WQ source
            if (source == null)
                continue;

            if (source.getC0() == 0.0)
                continue;

            double volout;

            // Find total flow volume leaving node
            if (!(qN.getNode() is Tank))
                volout = qN.getVolumeIn(); // Junctions
            else  // Tanks
                volout = qN.getVolumeIn() - qN.getDemand() * dt;

            double qout = volout / dt;

            double massadded = 0;
            // Evaluate source input only if node outflow > cutoff flow
            if (qout > qcutoff) {
                // Mass added depends on type of source
                double s = sourcequal(source);
                switch (source.getType()) {
                    case Source.Type.CONCEN:
                        // Only add source mass if demand is negative
                        if (qN.getDemand() < 0.0) {
                            massadded = -s * qN.getDemand() * dt;
                            if (qN.getNode() is Tank)
                                qN.setQuality(0.0);
                        } else
                            massadded = 0.0;
                        break;
                    // Mass Inflow Booster Source:
                    case Source.Type.MASS:
                        massadded = s * dt;
                        break;
                    // Setpoint Booster Source:
                    // Mass added is difference between source
                    // & node concen. times outflow volume
                    case Source.Type.SETPOINT:
                        if (s > qN.getQuality())
                            massadded = (s - qN.getQuality()) * volout;
                        else
                            massadded = 0.0;
                        break;
                    // Flow-Paced Booster Source:
                    // Mass added = source concen. times outflow volume
                    case Source.Type.FLOWPACED:
                        massadded = s * volout;
                        break;
                }

                // Source concen. contribution = (mass added / outflow volume)
                qN.setSourceContribution(massadded / volout);

                // Update total mass added for time period & simulation
                qN.setMassRate(qN.getMassRate() + massadded);
                if (Htime >= pMap.getRstart())
                    Wsource += massadded;
            }
        }

        // Add mass inflows from reservoirs to Wsource
        if (Htime >= pMap.getRstart()) {
            foreach (QualityTank qT  in  tanks) {
                if (((Tank) qT.getNode()).getArea() == 0.0) {
                    double volout = qT.getVolumeIn() - qT.getDemand() * dt;
                    if (volout > 0.0)
                        Wsource += volout * qT.getConcentration();
                }
            }
        }
    }

    ///<summary>Determines source concentration in current time period.</summary>
    private double sourcequal(Source source) {
        long k;
        double c;

        c = source.getC0();

        if (source.getType() == Source.Type.MASS)
            c /= 60.0;
        else
            c /= fMap.getUnits(FieldsMap.Type.QUALITY);


        Pattern pat = source.getPattern();
        if (pat == null)
            return (c);
        k = ((Qtime + pMap.getPstart()) / pMap.getPstep()) % pat.getFactorsList().Count;
        return (c * pat.getFactorsList()[(int)k]);
    }

    /**
     * Complete mix tank model.
     *
     * @param tank Tank to be updated.
     * @param dt   step duration in seconds.
     */
    private void tankmix1(QualityTank tank, long dt) {
        // React contents of tank
        double c = tankreact(tank.getConcentration(), tank.getVolume(), ((Tank) tank.getNode()).getKb(), dt);

        // Determine tank & volumes
        double vold = tank.getVolume();

        tank.setVolume(tank.getVolume() + tank.getDemand() * dt);

        double vin = tank.getVolumeIn();

        double cin;
        if (vin > 0.0)
            cin = tank.getMassIn() / vin;
        else
            cin = 0.0;

        // Compute inflow concen.
        double cmax = Math.Max(c, cin);

        // Mix inflow with tank contents
        if (vin > 0.0)
            c = (c * vold + cin * vin) / (vold + vin);
        c = Math.Min(c, cmax);
        c = Math.Max(c, 0.0);
        tank.setConcentration(c);
        tank.setQuality(tank.getConcentration());
    }

    /**
     * 2-compartment tank model (seg1 = mixing zone,seg2 = ambient zone).
     *
     * @param tank Tank to be updated.
     * @param dt   step duration in seconds.
     */
    private void tankmix2(QualityTank tank, long dt) {
        QualitySegment seg1, seg2;

        if (tank.getSegments().Count == 0)
            return;

        seg1 = tank.getSegments().Last.Value;
        seg2 = tank.getSegments().First.Value;

        seg1.c = tankreact(seg1.c, seg1.v, ((Tank) tank.getNode()).getKb(), dt);
        seg2.c = tankreact(seg2.c, seg2.v, ((Tank) tank.getNode()).getKb(), dt);

        // Find inflows & outflows
        double vnet = tank.getDemand() * dt;
        double vin = tank.getVolumeIn();

        double cin;
        if (vin > 0.0)
            cin = tank.getMassIn() / vin;
        else
            cin = 0.0;

        double v1max = ((Tank) tank.getNode()).getV1max();

        // Tank is filling
        double vt = 0.0;
        if (vnet > 0.0) {
            vt = Math.Max(0.0, (seg1.v + vnet - v1max));
            if (vin > 0.0) {
                seg1.c = ((seg1.c) * (seg1.v) + cin * vin) / (seg1.v + vin);
            }
            if (vt > 0.0) {
                seg2.c = ((seg2.c) * (seg2.v) + (seg1.c) * vt) / (seg2.v + vt);
            }
        }

        // Tank is emptying
        if (vnet < 0.0) {
            if (seg2.v > 0.0) {
                vt = Math.Min(seg2.v, (-vnet));
            }
            if (vin + vt > 0.0) {
                seg1.c = ((seg1.c) * (seg1.v) + cin * vin + (seg2.c) * vt) / (seg1.v + vin + vt);
            }
        }

        // Update segment volumes
        if (vt > 0.0) {
            seg1.v = v1max;
            if (vnet > 0.0)
                seg2.v += vt;
            else
                seg2.v = Math.Max(0.0, ((seg2.v) - vt));
        } else {
            seg1.v += vnet;
            seg1.v = Math.Min(seg1.v, v1max);
            seg1.v = Math.Max(0.0, seg1.v);
            seg2.v = 0.0;
        }

        tank.setVolume(Math.Max(tank.getVolume() + vnet, 0.0));
        // Use quality of mixed compartment (seg1) to
        // represent quality of tank since this is where
        // outflow begins to flow from
        tank.setConcentration(seg1.c);
        tank.setQuality(tank.getConcentration());
    }

    /**
     * First-In-First-Out (FIFO) tank model.
     *
     * @param tank Tank to be updated.
     * @param dt   step duration in seconds.
     */
    private void tankmix3(QualityTank tank, long dt) {

        if (tank.getSegments().Count == 0)
            return;

        // React contents of each compartment
        if (Reactflag) {
            foreach (QualitySegment seg  in  tank.getSegments()) {
                seg.c = tankreact(seg.c, seg.v, ((Tank) tank.getNode()).getKb(), dt);
            }
        }

        // Find inflows & outflows
        double vnet = tank.getDemand() * dt;
        double vin = tank.getVolumeIn();
        double vout = vin - vnet;
        double cin;

        if (vin > 0.0)
            cin = tank.getMassIn() / tank.getVolumeIn();
        else
            cin = 0.0;

        tank.setVolume(Math.Max(tank.getVolume() + vnet, 0.0));

        // Withdraw flow from first segment
        double vsum = 0.0;
        double csum = 0.0;

        while (vout > 0.0) {
            if (tank.getSegments().Count == 0)
                break;

            QualitySegment seg = tank.getSegments().First.Value;
            double vseg = seg.v;  // Flow volume from leading seg
            vseg = Math.Min(vseg, vout);
            if (tank.getSegments().Count == 1) vseg = vout;
            vsum += vseg;
            csum += (seg.c) * vseg;
            vout -= vseg; // Remaining flow volume
            if (vout >= 0.0 && vseg >= seg.v) {
                tank.getSegments().RemoveFirst();
            } else {
                // Remaining volume in segment
                seg.v -= vseg;
            }
        }

        // Use quality withdrawn from 1st segment
        // to represent overall quality of tank
        if (vsum > 0.0)
            tank.setConcentration(csum / vsum);
        else
            tank.setConcentration(tank.getSegments().First.Value.c);

        tank.setQuality(tank.getConcentration());

        // Add new last segment for new flow entering tank
        if (vin > 0.0) {
            if (tank.getSegments().Count > 0) {
                QualitySegment seg = tank.getSegments().Last.Value;

                // Quality is the same, so just add flow volume to last seg
                if (Math.Abs(seg.c - cin) < pMap.getCtol())
                    seg.v += vin;
                else // Otherwise add a new seg to tank
                    tank.getSegments().AddLast(new QualitySegment(vin, cin));
            } else //  If no segs left then add a new one.
                tank.getSegments().AddLast(new QualitySegment(vin, cin));
        }
    }

    /**
     * Last In-First Out (LIFO) tank model.
     *
     * @param tank Tank to be updated.
     * @param dt   step duration in seconds.
     */
    private void tankmix4(QualityTank tank, long dt) {
        if (tank.getSegments().Count == 0)
            return;

        // React contents of each compartment
        if (Reactflag) {
            for (LinkedListNode<QualitySegment> el = tank.getSegments().Last; el != null; el = el.Previous)
            {
                QualitySegment seg = el.Value;
                seg.c = this.tankreact(seg.c, seg.v, ((Tank)tank.getNode()).getKb(), dt);
            }
        }

        // Find inflows & outflows
        double vnet = tank.getDemand() * dt;
        double vin = tank.getVolumeIn();
        double cin;

        if (vin > 0.0)
            cin = tank.getMassIn() / tank.getVolumeIn();
        else
            cin = 0.0;

        tank.setVolume(Math.Max(0.0, tank.getVolume() + vnet));
        tank.setConcentration(tank.getSegments().Last.Value.c);

        // If tank filling, then create new last seg
        if (vnet > 0.0) {
            if (tank.getSegments().Count > 0) {
                QualitySegment seg = tank.getSegments().Last.Value;
                // Quality is the same, so just add flow volume to last seg
                if (Math.Abs(seg.c - cin) < pMap.getCtol())
                    seg.v += vnet;
                    // Otherwise add a new last seg to tank
                    // Which points to old last seg
                else
                    tank.getSegments().AddLast(new QualitySegment(vin, cin));
            } else
                tank.getSegments().AddLast(new QualitySegment(vin, cin));

            tank.setConcentration(tank.getSegments().Last.Value.c);
        }
        // If net emptying then remove last segments until vnet consumed
        else if (vnet < 0.0) {
            double vsum = 0.0;
            double csum = 0.0;
            vnet = -vnet;

            while (vnet > 0.0) {

                if (tank.getSegments().Count == 0)
                    break;

                QualitySegment seg = tank.getSegments().Last.Value;
                if (seg == null)
                    break;

                double vseg = seg.v;
                vseg = Math.Min(vseg, vnet);
                if (tank.getSegments().Count == 1)
                    vseg = vnet;

                vsum += vseg;
                csum += (seg.c) * vseg;
                vnet -= vseg;

                if (vnet >= 0.0 && vseg >= seg.v) {
                    tank.getSegments().RemoveLast();//(2.00.12 - LR)
                } else {
                    // Remaining volume in segment
                    seg.v -= vseg;
                }
            }
            // Reported tank quality is mixture of flow released and any inflow
            tank.setConcentration((csum + tank.getMassIn()) / (vsum + vin));
        }
        tank.setQuality(tank.getConcentration());
    }

    ///<summary>Computes new quality in a tank after reaction occurs.</summary>
    private double tankreact(double c, double v, double kb, long dt) {
        double cnew, dc, rbulk;

        if (!Reactflag)
            return (c);

        if (qualflag == PropertiesMap.QualType.AGE)
            return (c + dt / 3600.0);

        rbulk = bulkrate(c, kb, pMap.getTankOrder()) * Tucf;

        dc = rbulk * dt;
        if (Htime >= pMap.getRstart())
            Wtank += Math.Abs(dc) * v;
        cnew = c + dc;
        cnew = Math.Max(0.0, cnew);
        return (cnew);
    }

    ///<summary>Transports constituent mass through pipe network under a period of constant hydraulic conditions.</summary>
    private void transport(long tstep) {
        long qtime = 0, dt;
        while (qtime < tstep) {
            dt = Math.Min(pMap.getQstep(), tstep - qtime);
            qtime += dt;
            if (Reactflag) updatesegs(dt);
            accumulate(dt);
            updatenodes(dt);
            sourceinput(dt);
            release(dt);
        }
        updatesourcenodes(tstep);
    }



    /**
     * Updates concentration at all nodes to mixture of accumulated inflow from connecting pipes.
     *
     * @param dt step duration in seconds
     */
    private void updatenodes(long dt) {
        foreach (QualityNode qN  in  juncs) {
            if (qN.getDemand() < 0.0)
                qN.setVolumeIn(qN.getVolumeIn() - qN.getDemand() * dt);
            if (qN.getVolumeIn() > 0.0)
                qN.setQuality(qN.getMassIn() / qN.getVolumeIn());
            else
                qN.setQuality(qN.getSourceContribution());
        }

        //  Update tank quality
        updatetanks(dt);

        // For flow tracing, set source node concen. to 100.
        if (qualflag == PropertiesMap.QualType.TRACE)
            traceNode.setQuality(100.0);
    }

    /**
     * Reacts material in pipe segments up to time t.
     *
     * @param dt step duration in seconds.
     */
    private void updatesegs(long dt) {
        foreach (QualityLink qL  in  links) {
            double rsum = 0.0;
            double vsum = 0.0;
            if (qL.getLink().getLenght() == 0.0)
                continue;

            foreach (QualitySegment seg  in  qL.getSegments()) {
                double cseg = seg.c;
                seg.c = pipereact(qL, seg.c, seg.v, dt);

                if (qualflag == PropertiesMap.QualType.CHEM) {
                    rsum += Math.Abs((seg.c - cseg)) * seg.v;
                    vsum += seg.v;
                }
            }

            if (vsum > 0.0)
                qL.setFlowResistance(rsum / vsum / dt * Constants.SECperDAY);
            else
                qL.setFlowResistance(0.0);
        }
    }

    /**
     * Updates quality at source nodes.
     *
     * @param dt step duration in seconds.
     */
    private void updatesourcenodes(long dt) {
        Source source;

        if (qualflag != PropertiesMap.QualType.CHEM) return;


        foreach (QualityNode qN  in  nodes) {
            source = qN.getNode().getSource();
            if (source == null)
                continue;

            qN.setQuality(qN.getQuality() + qN.getSourceContribution());


            if (qN.getNode() is Tank) {
                if (((Tank) qN.getNode()).getArea() > 0.0)
                    qN.setQuality(((Tank) qN.getNode()).getConcentration()[0]);
            }

            qN.setMassRate(qN.getMassRate() / dt);
        }
    }

    /**
     * Updates tank volumes & concentrations.
     *
     * @param dt step duration in seconds.
     */
    private void updatetanks(long dt) {
        // Examine each reservoir & tank
        foreach (QualityTank tank  in  tanks) {
            // Use initial quality for reservoirs
            if (((Tank) tank.getNode()).getArea() == 0.0) {

                tank.setQuality(tank.getNode().getC0()[0]);
            } else {
                // Update tank WQ based on mixing model
                switch (((Tank) tank.getNode()).getMixModel()) {
                    case Tank.MixType.MIX2:
                        tankmix2(tank, dt);
                        break;
                    case Tank.MixType.FIFO:
                        tankmix3(tank, dt);
                        break;
                    case Tank.MixType.LIFO:
                        tankmix4(tank, dt);
                        break;
                    default:
                        tankmix1(tank, dt);
                        break;
                }
            }
        }
    }

    ///<summary>Computes wall reaction rate.</summary>
    private double wallrate(double c, double d, double kw, double kf) {
        if (kw == 0.0 || d == 0.0)
            return (0.0);
        if (pMap.getWallOrder() == 0.0) {
            kf = Utilities.getSignal(kw) * c * kf;
            kw = kw * Math.Pow(elevUnits, 2);
            if (Math.Abs(kf) < Math.Abs(kw))
                kw = kf;
            return (kw * 4.0 / d);
        } else
            return (c * kf);
    }

    public List<QualityNode> getnNodes(){
        return nodes;
    }

    public List<QualityLink> getnLinks(){
        return links;
    }
}
}