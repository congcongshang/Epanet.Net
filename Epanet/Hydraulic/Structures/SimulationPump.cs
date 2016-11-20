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
using Epanet.Network;
using Epanet.Network.Structures;

namespace Epanet.Hydraulic.Structures {

    public class SimulationPump:SimulationLink {
        public SimulationPump(List<SimulationNode> indexedNodes, Link @ref, int idx):base(indexedNodes, @ref, idx) {

            for (int i = 0; i < 6; i++)
                this.energy[i] = ((Pump)@ref).Energy[0]; // BUG: Baseform bug ?

            this.H0 = ((Pump)@ref).H0;
            this.FlowCoefficient = ((Pump)@ref).FlowCoefficient;
            this.N = ((Pump)@ref).N;
        }

        private readonly double[] energy = {0, 0, 0, 0, 0, 0};

        public Pump.PumpType Ptype { get { return ((Pump)this.link).Ptype; } }

        public double Q0 { get { return ((Pump)this.link).Q0; } }

        public double Qmax { get { return ((Pump)this.link).Qmax; } }

        public double Hmax { get { return ((Pump)this.link).Hmax; } }

        public Curve Hcurve { get { return ((Pump)this.link).HCurve; } }

        public Curve Ecurve { get { return ((Pump)this.link).ECurve; } }

        public Pattern Upat { get { return ((Pump)this.link).UPat; } }

        public Pattern Epat { get { return ((Pump)this.link).EPat; } }

        public double Ecost { get { return ((Pump)this.link).ECost; } }

        // Simulation getters and setters
        public double[] Energy { get { return this.energy; } }

        ///<summary>Simulated shutoff head</summary>
        public double H0 { set; get; }

        ///<summary>Simulated Flow coefficent</summary>
        private double FlowCoefficient { get; set; }

        ///<summary>Simulated flow expoent</summary>
        public double N { set; get; }

        /// <summary>Computes flow energy associated with this link pump.</summary>
        /// <param name="pMap"></param>
        /// <param name="fMap"></param>
        /// <param name="power">Pump used power (KW)</param>
        /// <param name="efficiency">Pump effiency</param>
        private void GetFlowEnergy(PropertiesMap pMap, FieldsMap fMap, out double power, out double efficiency) {
            power = efficiency = 0.0;

            if (this.status <= Link.StatType.CLOSED) {
                return;
            }

            double q = Math.Abs(this.flow);
            double dh = Math.Abs(this.first.SimHead - this.second.SimHead);

            double e = pMap.EPump;

            if (this.Ecurve != null) {
                Curve curve = this.Ecurve;
                e = curve[q * fMap.GetUnits(FieldsMap.FieldType.FLOW)];
            }

            e = Math.Min(e, 100.0);
            e = Math.Max(e, 1.0);
            e /= 100.0;

            power = dh * q * pMap.SpGrav / 8.814 / e * Constants.KWperHP;
            efficiency = e;
        }


        /// <summary>Accumulates pump energy usage.</summary>
        private double UpdateEnergy(
            PropertiesMap pMap,
            FieldsMap fMap,
            long n,
            double c0,
            double f0,
            double dt) {
            //Skip closed pumps
            if (this.status <= Link.StatType.CLOSED) return 0.0;
            double q = Math.Max(Constants.QZERO, Math.Abs(this.flow));

            // Find pump-specific energy cost
            double c = this.Ecost > 0.0 ? this.Ecost : c0;

            if (this.Epat != null) {
                int m = (int)(n % this.Epat.FactorsList.Count);
                c *= this.Epat.FactorsList[m];
            }
            else
                c *= f0;

            // Find pump energy & efficiency
            double power, efficiency;
            this.GetFlowEnergy(pMap, fMap, out power, out efficiency);

            // Update pump's cumulative statistics
            this.energy[0] = this.energy[0] + dt; // Time on-line
            this.energy[1] = this.energy[1] + efficiency * dt; // Effic.-hrs
            this.energy[2] = this.energy[2] + power / q * dt; // kw/cfs-hrs
            this.energy[3] = this.energy[3] + power * dt; // kw-hrs
            this.energy[4] = Math.Max(this.energy[4], power);
            this.energy[5] = this.energy[5] + c * power * dt; // cost-hrs.

            return power;
        }

        /// <summary>Computes P and Y coeffs. for pump in the link.</summary>
        public void ComputePumpCoeff(FieldsMap fMap, PropertiesMap pMap) {
            if (this.status <= Link.StatType.CLOSED || this.setting == 0.0) {
                this.invHeadLoss = 1.0 / Constants.CBIG;
                this.flowCorrection = this.flow;
                return;
            }

            double q = Math.Max(Math.Abs(this.flow), Constants.TINY);

            if (this.Ptype == Pump.PumpType.CUSTOM) {
                double hh0, rr;
                this.Hcurve.GetCoeff(fMap, q / this.setting, out hh0, out rr);

                this.H0 = -hh0;
                this.FlowCoefficient = -rr;
                this.N = 1.0;
            }

            double h0 = this.setting * this.setting * this.H0;
            double n = this.N;
            double r = this.FlowCoefficient * Math.Pow(this.setting, 2.0 - n);
            if (n != 1.0) r = n * r * Math.Pow(q, n - 1.0);

            this.invHeadLoss = 1.0 / Math.Max(r, pMap.RQtol);
            this.flowCorrection = this.flow / n + this.invHeadLoss * h0;
        }

        /// <summary>Get new pump status.</summary>
        /// <param name="pMap"></param>
        /// <param name="dh">head gain</param>
        /// <returns></returns>
        public Link.StatType PumpStatus(PropertiesMap pMap, double dh) {
            double hmax;

            if (this.Ptype == Pump.PumpType.CONST_HP)
                hmax = Constants.BIG;
            else
                hmax = this.setting * this.setting * this.Hmax;

            if (dh > hmax + pMap.HTol)
                return Link.StatType.XHEAD;

            return Link.StatType.OPEN;
        }

        /// <summary>Update pumps energy.</summary>
        public static double StepEnergy(
            PropertiesMap pMap,
            FieldsMap fMap,
            Pattern epat,
            List<SimulationPump> pumps,
            long htime,
            long hstep) {
            double dt, psum = 0.0;


            if (pMap.Duration == 0)
                dt = 1.0;
            else if (htime < pMap.Duration)
                dt = (double)hstep / 3600.0;
            else
                dt = 0.0;

            if (dt == 0.0)
                return 0.0;

            long n = (htime + pMap.PStart) / pMap.PStep;


            double c0 = pMap.ECost;
            double f0 = 1.0;

            if (epat != null) {
                long m = n % (long)epat.FactorsList.Count;
                f0 = epat.FactorsList[(int)m];
            }

            foreach (SimulationPump pump  in  pumps) {
                psum += pump.UpdateEnergy(pMap, fMap, n, c0, f0, dt);
            }

            return psum;
        }


    }

}