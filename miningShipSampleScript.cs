




        private DateTime jobStartTime;
        private DateTime jobProgressTime;
        private DateTime? jobEndTime;
        private float lastHingePosition;
        private float lastMiningHeadDepth;

        IMyCargoContainer cargo;
        List<IMyMotorAdvancedStator> hinges = new List<IMyMotorAdvancedStator>();
        List<IMyPistonBase> mainPistons = new List<IMyPistonBase>();
        List<IMyPistonBase> secondaryPistons = new List<IMyPistonBase>();
        List<IMyShipDrill> drills = new List<IMyShipDrill>();
        private IMyMotorAdvancedStator measuresHinge;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;//WARNING piston adjustment is tuned to Update100!!!

            GridTerminalSystem.GetBlockGroupWithName("hinges").GetBlocksOfType(hinges);
            cargo = (IMyCargoContainer)GridTerminalSystem.GetBlockWithName("Large Cargo Container BR raw mats");
            GridTerminalSystem.GetBlockGroupWithName("Main Pistons").GetBlocksOfType(mainPistons);
            GridTerminalSystem.GetBlockGroupWithName("Secondary Pistons").GetBlocksOfType(secondaryPistons);
            GridTerminalSystem.GetBlockGroupWithName("drills").GetBlocksOfType(drills);
            measuresHinge = hinges.First();
            lastHingePosition = ConvertRadiansToDegrees(measuresHinge.Angle);
            lastMiningHeadDepth = GetMiningHeadDepth();
            hinges.ForEach(x => x.TargetVelocityRPM = -.7f);
        }

        private Promise promise = Promise.Defer();
        private Promise mainMiningOpPromise = Promise.Defer();
        private Promise cargoCheckPromise = Promise.Defer();
        private Promise hingeSpeedPromise = Promise.Defer();

        public void Main(string argument, UpdateType updateSource)
        {
            if (argument == "reset")
            {
                promise = Promise.Defer();
                mainMiningOpPromise = Promise.Defer();
                cargoCheckPromise = Promise.Defer();
                hingeSpeedPromise = Promise.Defer();
                jobEndTime = null;
            }

            promise
                .Then(() => jobStartTime = DateTime.Now)
                .Then((p) =>
                {
                    Echo($"Start: {jobStartTime}");

                    if (!jobEndTime.HasValue)
                        jobProgressTime = DateTime.Now;

                    Echo($"Elapsed: {jobProgressTime - jobStartTime}");
                    Echo($"Progress: {GetPistonExtPercentage()}%");

                    return Init(p);
                })
                .Then(MainOperation)
                .Then(p =>
                {
                    return GoHome(p).Then(() =>
                    {
                        jobEndTime = DateTime.Now;
                    });
                    
                }).Then(x =>
                {
                    Echo($"End: {jobEndTime}");
                    return x;
                });
        }

        private float GetPistonExtPercentage()
        {
            var maxDistance = (mainPistons.Count + secondaryPistons.Count) * 10f;
            var currentDistance = mainPistons.Sum(x => x.CurrentPosition) +
                                  secondaryPistons.Sum(x => 10f - x.CurrentPosition);
            return (currentDistance / maxDistance) * 100f;
        }

        private Promise WhenHingeAtHomeSide(Promise p)
        {
            var currentDegrees = ConvertRadiansToDegrees(measuresHinge.Angle);
            return currentDegrees < -64f ? p.Resolve() : p;
        }

        private Promise WhenHingeAtFarSide(Promise p)
        {
            var currentDegrees = ConvertRadiansToDegrees(measuresHinge.Angle);
            return currentDegrees > 64f ? p.Resolve() : p;
        }

        private void StopPistons()
        {
            secondaryPistons.ForEach(x => x.Enabled = false);
            mainPistons.ForEach(x => x.Enabled = false);
        }

        private Promise IsCargoEmpty(Promise p)
        {
            var maxVolume = cargo.GetInventory().MaxVolume.RawValue;
            var currentVolume = cargo.GetInventory().CurrentVolume.RawValue;
            Echo($"EMPTY CHECK: maxVolume: {maxVolume}");
            Echo($"EMPTY CHECK: currentVolume: {currentVolume}");
            if (currentVolume <= (maxVolume * .05))
            {
                Echo("CARGO IS EMPTY");
                return p.Resolve();
            }
            else
            {
                Echo("CARGO NOT EMPTY");
                return p;
            }
        }

        private Promise IsCargoCapacityReached(Promise p)
        {
            var maxVolume = cargo.GetInventory().MaxVolume.RawValue;
            var currentVolume = cargo.GetInventory().CurrentVolume.RawValue;
            Echo($"FULL CHECK: maxVolume: {maxVolume}");
            Echo($"FULL CHECK: currentVolume: {currentVolume}");
            if ((currentVolume >= (maxVolume * .90)))
            {
                Echo("CARGO IS FULL");
                return p.Resolve();
            }
            else
            {
                Echo("CARGO NOT FULL");
                return p;
            }
        }

        private bool MiningHeadStoppedMoving => Math.Abs(GetMiningHeadDepth() - lastMiningHeadDepth) <= 1;

        private Promise PrimaryPistonsStoppedMoving(Promise p)
        {
            if (MiningHeadStoppedMoving)
            {
                return p.Resolve();
            }

            lastMiningHeadDepth = GetMiningHeadDepth();
            return p;
        }

        private float GetMiningHeadDepth()
        {
            var main = mainPistons.Sum(x => x.CurrentPosition);
            var second = secondaryPistons.Select(x => x.CurrentPosition - 10f).Sum();
            return main + second;
        }

        float ConvertRadiansToDegrees(float radians)
        {
            var degrees = (180 / Math.PI) * radians;
            return (float)degrees;
        }

        private Promise GoHome(Promise promise)
        {
            return promise
                .Then(MoveHingeToHomePositon)
                .Then((p) =>
                {
                    drills.ForEach(x => x.Enabled = false);
                    mainPistons.ForEach(x => x.Enabled = true);
                    mainPistons.ForEach(x => x.Retract());

                    secondaryPistons.ForEach(x => x.Enabled = true);
                    secondaryPistons.ForEach(x => x.Extend());
                    return p.Resolve();
                });
        }

        private Promise MainOperation(Promise promise)
        {
            return promise
                .Then(() =>
                {
                    hinges.ForEach(x => x.Enabled = true);
                    drills.ForEach(x => x.Enabled = true);
                })
                .Then(p =>
                {
                    hingeSpeedPromise.Then((x) =>
                    {
                        var angleDegrees = ConvertRadiansToDegrees(measuresHinge.Angle);
                        var inSlowZone = angleDegrees < 44 && angleDegrees > -44;
                        if (inSlowZone)
                        {
                            SetHingeSpeedAbs(.5f);
                            return x;
                        }

                        SetHingeSpeedAbs(1.2f);

                        return x;
                    });

                    mainMiningOpPromise.Then(WhenHingeAtFarSide)
                        .Then(() =>
                        {
                            LowerMiningHead();
                            ReverseHinge();
                        })
                        .Then(StopPistons)
                        .Then(WhenHingeAtHomeSide)
                        .Then(() =>
                        {
                            LowerMiningHead();
                            ReverseHinge();
                        })
                        .Then(StopPistons)
                        .Then(x => mainMiningOpPromise.Repeat());

                    cargoCheckPromise.Then(IsCargoCapacityReached)
                        .Then(() =>
                        {
                            hinges.ForEach(x => x.Enabled = false);
                            drills.ForEach(x => x.Enabled = false);

                            //just in case!
                            secondaryPistons.ForEach(x => x.Enabled = false);
                            mainPistons.ForEach(x => x.Enabled = false);
                        })
                        .Then(IsCargoEmpty)
                        .Then(() =>
                        {
                            hinges.ForEach(x => x.Enabled = true);
                            drills.ForEach(x => x.Enabled = true);
                        }).Then(x => cargoCheckPromise.Repeat());

                    if (mainPistons.All(x => x.CurrentPosition >= x.MaxLimit))
                    {
                        return p.Resolve();
                    }

                    return p;
                });
        }

        private void SetHingeSpeedAbs(float f)
        {
            var sign = measuresHinge.TargetVelocityRPM > 0 ? 1 : -1;
            var nextVelocity = f * sign;
            hinges.ForEach(x => x.TargetVelocityRPM = nextVelocity);
        }

        private Promise Init(Promise promise)
        {
            return promise
                .Then(() =>
                {
                    hinges.ForEach(x => x.Enabled = true);
                    drills.ForEach(x => x.Enabled = false);
                })
                .Then(() =>
                {
                    var currentAngle = ConvertRadiansToDegrees(measuresHinge.Angle);
                    if (currentAngle == lastHingePosition)
                    {
                        ReverseHinge();
                    }

                    lastHingePosition = currentAngle;
                })
                .Then(MoveHingeToDownPosition)
                .Then(() =>
                {
                    secondaryPistons.ForEach(x => x.Enabled = true);
                    secondaryPistons.ForEach(x => x.Retract());
                }).Then(SecondaryPistonsStoppedMoving)
                .Then(PrimaryPistonsStoppedMoving)
                .Then(StopPistons)
                .Then(MoveHingeToHomePositon)
                .Then(HingeIsAtHomePosition)
                .Then(ReverseHinge);
        }

        private Promise MoveHingeToDownPosition(Promise p)
        {
            var currentAngle = ConvertRadiansToDegrees(measuresHinge.Angle);

            Echo($"currentAngle: {currentAngle}");

            if (currentAngle <= 3f && currentAngle >= -3f)
            {
                hinges.ForEach(x => x.Enabled = false);
                Echo("MoveHingeToDownPosition RETURN TRUE");
                return p.Resolve();
            }

            return p;
        }

        private void ReverseHinge() => hinges.ForEach(x => x.TargetVelocityRPM = x.TargetVelocityRPM * -1);

        private Promise SecondaryPistonsStoppedMoving(Promise p)
        {
            Echo($"lastMiningHeadDepth {lastMiningHeadDepth}");
            Echo($"GetMiningHeadDepth() {GetMiningHeadDepth()}");

            //cast to int rounding
            if (MiningHeadStoppedMoving)
            {
                if (secondaryPistons.All(x => x.CurrentPosition == x.MinLimit))
                {
                    mainPistons.ForEach(x => x.Enabled = true);
                    mainPistons.ForEach(x => x.Extend());
                }

                return p.Resolve();
            }

            lastMiningHeadDepth = GetMiningHeadDepth();

            return p;
        }

        private void MoveHingeToHomePositon()
        {
            hinges.ForEach(x => x.Enabled = true);
            if (measuresHinge.TargetVelocityRPM > 0)
            {
                ReverseHinge();
            }

            drills.ForEach(x => x.Enabled = true);
        }

        private Promise HingeIsAtHomePosition(Promise p)
        {
            return ConvertRadiansToDegrees(measuresHinge.Angle) == measuresHinge.LowerLimitDeg || ConvertRadiansToDegrees(measuresHinge.Angle) == measuresHinge.UpperLimitDeg 
                ? p.Resolve() : p;
        }

        private void LowerMiningHead()
        {
            //secondary pistons are retracted to lower the mining head
            //main pistons are extended to lower the mining head

            var isSecondaryFullyRetracted = secondaryPistons.All(x => x.CurrentPosition <= x.MinLimit);

            if (!isSecondaryFullyRetracted)
            {
                secondaryPistons.ForEach(x => x.Enabled = true);
                secondaryPistons.ForEach(x => x.Retract());
                return;
            }

            mainPistons.ForEach(x => x.Enabled = true);
            mainPistons.ForEach(x => x.Extend());
        }




        public class Promise
        {
            public static Promise Defer()
            {
                var p = new Promise();
                p.IsResolved = true;
                return p;
            }

            private bool IsExecuting { get; set; }
            private Promise Next { get; set; }
            public bool IsResolved { get; set; }
            public string Tag { get; set; }

            public Promise Then(Func<Promise, Promise> deferred)
            {
                if (Next == null)
                {
                    Next = new Promise();
                }

                if (Next.IsResolved) return Next;

                if (IsResolved && !Next.IsResolved || IsExecuting)
                {
                    Next.IsExecuting = true;
                    var result = deferred(Next);
                    Next.IsExecuting = false;
                    return result;
                }

                return this;
            }

            public Promise Then(Action deferred)
            {
                return Then(x =>
                {
                    deferred();
                    return x.Resolve();
                });
            }

            public Promise Resolve()
            {
                var skipOne = false;
                IsResolved = true;
                return Then(x =>
                {
                    if (!skipOne)
                    {
                        skipOne = true;
                        return x;
                    }

                    x.IsResolved = true;
                    return x;
                });
            }

            public Promise Repeat()
            {
                Reset(this);
                return this;
            }

            private void Reset(Promise p)
            {
                if (p.Next != null)
                {
                    p.Next.IsResolved = false;
                    Reset(p.Next);
                }
            }
        }

