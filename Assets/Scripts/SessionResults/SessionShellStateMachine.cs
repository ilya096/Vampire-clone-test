using System.Collections.Generic;

namespace LogoSurvivor.SessionResults
{
    public sealed class SessionShellStateMachine
    {
        private readonly Stack<SessionShellState> _navigation = new();
        private SessionShellState _pauseReturnState = SessionShellState.Gameplay;
        private bool _pauseLayerActive;

        public SessionShellState Current { get; private set; } = SessionShellState.Start;
        public bool IsPauseLayerActive => _pauseLayerActive;

        public bool StartGame()
        {
            if (Current != SessionShellState.Start)
            {
                return false;
            }

            Current = SessionShellState.Gameplay;
            return true;
        }

        public bool Pause()
        {
            if (Current is not (SessionShellState.Gameplay or SessionShellState.Result))
            {
                return false;
            }

            _navigation.Clear();
            _pauseReturnState = Current;
            _pauseLayerActive = true;
            Current = SessionShellState.Pause;
            return true;
        }

        public bool Continue()
        {
            if (Current != SessionShellState.Pause)
            {
                return false;
            }

            Current = _pauseReturnState;
            _pauseLayerActive = false;
            _navigation.Clear();
            return true;
        }

        public bool OpenSettings()
        {
            if (Current is not (SessionShellState.Start or SessionShellState.Pause))
            {
                return false;
            }

            return OpenOverlay(SessionShellState.Settings);
        }

        public bool OpenCredits()
        {
            if (Current != SessionShellState.Start)
            {
                return false;
            }

            return OpenOverlay(SessionShellState.Credits);
        }

        public bool OpenDevelopmentHub()
        {
            return Current == SessionShellState.Pause
                && OpenOverlay(SessionShellState.DevelopmentHub);
        }

        public bool OpenDevelopmentParameters()
        {
            return Current == SessionShellState.DevelopmentHub
                && OpenOverlay(SessionShellState.DevelopmentParameters);
        }

        public bool OpenDevelopmentSpecialCards()
        {
            return Current == SessionShellState.DevelopmentHub
                && OpenOverlay(SessionShellState.DevelopmentSpecialCards);
        }

        public bool OpenDevelopmentLog()
        {
            return Current is SessionShellState.Pause or SessionShellState.DevelopmentHub
                && OpenOverlay(SessionShellState.DevelopmentLog);
        }

        public bool ShowResult()
        {
            if (Current is not (SessionShellState.Gameplay or SessionShellState.Pause))
            {
                return false;
            }

            _navigation.Clear();
            _pauseLayerActive = false;
            Current = SessionShellState.Result;
            return true;
        }

        public bool OpenExpandedCard()
        {
            if (Current != SessionShellState.Result)
            {
                return false;
            }

            return OpenOverlay(SessionShellState.ExpandedCard);
        }

        public bool OpenRestartConfirmation()
        {
            if (Current != SessionShellState.Pause)
            {
                return false;
            }

            return OpenOverlay(SessionShellState.ConfirmRestart);
        }

        public bool OpenExitConfirmation()
        {
            if (Current is not (SessionShellState.Pause or SessionShellState.Result))
            {
                return false;
            }

            return OpenOverlay(SessionShellState.ConfirmExit);
        }

        public bool CancelOverlay()
        {
            if (Current is not (SessionShellState.Settings
                or SessionShellState.Credits
                or SessionShellState.DevelopmentHub
                or SessionShellState.DevelopmentParameters
                or SessionShellState.DevelopmentSpecialCards
                or SessionShellState.DevelopmentLog
                or SessionShellState.ExpandedCard
                or SessionShellState.ConfirmRestart
                or SessionShellState.ConfirmExit))
            {
                return false;
            }

            if (_navigation.Count == 0)
            {
                return false;
            }

            Current = _navigation.Pop();
            return true;
        }

        public void ResetToStart()
        {
            _navigation.Clear();
            _pauseReturnState = SessionShellState.Gameplay;
            _pauseLayerActive = false;
            Current = SessionShellState.Start;
        }

        private bool OpenOverlay(SessionShellState target)
        {
            _navigation.Push(Current);
            Current = target;
            return true;
        }
    }
}
