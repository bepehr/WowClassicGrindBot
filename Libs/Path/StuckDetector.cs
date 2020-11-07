﻿using Libs.Goals;
using Libs.GOAP;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Libs
{
    public class StuckDetector
    {
        private readonly PlayerReader playerReader;
        private readonly WowProcess wowProcess;
        private readonly StopMoving stopMoving;
        private readonly ILogger logger;
        private readonly Random random = new Random();
        private readonly IPlayerDirection playerDirection;

        private WowPoint targetLocation = new WowPoint(0, 0);

        private Stopwatch LastReachedDestiationTimer = new Stopwatch();
        private Stopwatch LastUnstickAttemptTimer = new Stopwatch();
        private double previousDistanceToTarget = 99999;
        private DateTime timeOfLastSignificantMovement = DateTime.Now;

        public StuckDetector(PlayerReader playerReader, WowProcess wowProcess, IPlayerDirection playerDirection, StopMoving stopMoving, ILogger logger)
        {
            this.playerReader = playerReader;
            this.wowProcess = wowProcess;
            this.stopMoving = stopMoving;
            this.logger = logger;
            this.playerDirection = playerDirection;

            ResetStuckParameters();
        }

        public void SetTargetLocation(WowPoint targetLocation)
        {
            this.targetLocation = targetLocation;
            ResetStuckParameters();
        }

        public void ResetStuckParameters()
        {
            LastReachedDestiationTimer.Reset();
            LastReachedDestiationTimer.Start();

            LastUnstickAttemptTimer.Reset();
            LastUnstickAttemptTimer.Start();

            previousDistanceToTarget = 99999;
            timeOfLastSignificantMovement = DateTime.Now;

            //logger.LogInformation("ResetStuckParameters()");
        }

        public delegate void ActionEventHandler(object sender, ActionEventArgs e);

        public event ActionEventHandler? ActionEvent;

        public void SendActionEvent(ActionEventArgs e)
        {
            ActionEvent?.Invoke(this, e);
        }

        public int actionDurationSeconds => (int)(LastReachedDestiationTimer.ElapsedMilliseconds / 1000);
        public int unstickSeconds => (int)(LastUnstickAttemptTimer.ElapsedMilliseconds / 1000);

        public async Task Unstick()
        {
            await wowProcess.KeyPress(ConsoleKey.Spacebar, 500);

            logger.LogInformation($"Stuck for {actionDurationSeconds}s, last tried to unstick {unstickSeconds}s ago. Unstick seconds={unstickSeconds}.");

            if (actionDurationSeconds > 240)
            {
                // stuck for 4 minutes
                logger.LogInformation("Stuck for 4 minutes");
                SendActionEvent(new ActionEventArgs(GoapKey.abort, true));
                await Task.Delay(120000);
            }

            if (unstickSeconds > 5)
            {
                int strafeDuration = (int)(1000 + (((double)actionDurationSeconds * 1000) / 12));

                if (strafeDuration > 20000)
                {
                    strafeDuration = 20000;
                }

                if (actionDurationSeconds > 20)
                {
                    // back up a bit, added "remove" move forward
                    wowProcess.SetKeyState(ConsoleKey.DownArrow, true, false, "StuckDetector_back_up");
					wowProcess.SetKeyState(ConsoleKey.UpArrow, false, false, "StuckDetector");
                    await Task.Delay(strafeDuration);
                    wowProcess.SetKeyState(ConsoleKey.DownArrow, false, false, "StuckDetector");
                }
                this.stopMoving?.Stop();

                // stuck for 20 seconds
                var r = random.Next(0, 100);
                if (r < 50)
                {
                    logger.LogInformation($"Trying to unstick by strafing left for {strafeDuration}ms");
                    wowProcess.SetKeyState(ConsoleKey.A, true, false, "StuckDetector");
                    await Task.Delay(strafeDuration);
                    wowProcess.SetKeyState(ConsoleKey.A, false, false, "StuckDetector");
                }
                else
                {
                    logger.LogInformation($"Trying to unstick by strafing right for {strafeDuration}ms");
                    wowProcess.SetKeyState(ConsoleKey.D, true, false, "StuckDetector");
                    await Task.Delay(strafeDuration);
                    wowProcess.SetKeyState(ConsoleKey.D, false, false, "StuckDetector");
                }

                await wowProcess.TapStopKey();

                wowProcess.SetKeyState(ConsoleKey.UpArrow, true, false, "StuckDetector");

                var heading = DirectionCalculator.CalculateHeading(this.playerReader.PlayerLocation, targetLocation);
                await playerDirection.SetDirection(heading, targetLocation, "Move to next point");

                LastUnstickAttemptTimer.Reset();
                LastUnstickAttemptTimer.Start();
            }
            else
            {
                await wowProcess.KeyPress(ConsoleKey.Spacebar, 500);
            }
        }

        internal bool IsGettingCloser()
        {
            var currentDistanceToTarget = WowPoint.DistanceTo(this.playerReader.PlayerLocation, targetLocation);

            if (currentDistanceToTarget < previousDistanceToTarget - 5)
            {
                ResetStuckParameters();
                previousDistanceToTarget = currentDistanceToTarget;
                return true;
            }

            if (currentDistanceToTarget > previousDistanceToTarget + 5)
            {
                currentDistanceToTarget = previousDistanceToTarget;
            }

            if ((DateTime.Now - timeOfLastSignificantMovement).TotalSeconds > 3)
            {
                logger.LogInformation("We seem to be stuck!");
                return false;
            }

            return true;
        }

        internal bool IsMoving()
        {
            var currentDistanceToTarget = WowPoint.DistanceTo(this.playerReader.PlayerLocation, targetLocation);

            if (Math.Abs(currentDistanceToTarget - previousDistanceToTarget) > 1)
            {
                ResetStuckParameters();
                previousDistanceToTarget = currentDistanceToTarget;
                return true;
            }

            if ((DateTime.Now - timeOfLastSignificantMovement).TotalSeconds > 3)
            {
                logger.LogInformation("We seem to be stuck!");
                return false;
            }

            return true;
        }
    }
}