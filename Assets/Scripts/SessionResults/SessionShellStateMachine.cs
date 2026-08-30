namespace LogoSurvivor.SessionResults
{
    public sealed class SessionShellStateMachine
    {
        private SessionShellState _returnState = SessionShellState.Start;

        public SessionShellState Current { get; private set; } = SessionShellState.Start;

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
            if (Current != SessionShellState.Gameplay)
            {
                return false;
            }

            Current = SessionShellState.Pause;
            return true;
        }

        public bool Continue()
        {
            if (Current != SessionShellState.Pause)
            {
                return false;
            }

            Current = SessionShellState.Gameplay;
            return true;
        }

        public bool OpenSettings()
        {
            if (Current is not (SessionShellState.Start or SessionShellState.Pause))
            {
                return false;
            }

            _returnState = Current;
            Current = SessionShellState.Settings;
            return true;
        }

        public bool OpenCredits()
        {
            if (Current != SessionShellState.Start)
            {
                return false;
            }

            _returnState = Current;
            Current = SessionShellState.Credits;
            return true;
        }

        public bool ShowResult()
        {
            if (Current is not (SessionShellState.Gameplay or SessionShellState.Pause))
            {
                return false;
            }

            Current = SessionShellState.Result;
            return true;
        }

        public bool OpenExpandedCard()
        {
            if (Current != SessionShellState.Result)
            {
                return false;
            }

            _returnState = Current;
            Current = SessionShellState.ExpandedCard;
            return true;
        }

        public bool OpenRestartConfirmation()
        {
            if (Current != SessionShellState.Pause)
            {
                return false;
            }

            _returnState = Current;
            Current = SessionShellState.ConfirmRestart;
            return true;
        }

        public bool OpenExitConfirmation()
        {
            if (Current is not (SessionShellState.Pause or SessionShellState.Result))
            {
                return false;
            }

            _returnState = Current;
            Current = SessionShellState.ConfirmExit;
            return true;
        }

        public bool CancelOverlay()
        {
            if (Current is not (SessionShellState.Settings
                or SessionShellState.Credits
                or SessionShellState.ExpandedCard
                or SessionShellState.ConfirmRestart
                or SessionShellState.ConfirmExit))
            {
                return false;
            }

            Current = _returnState;
            return true;
        }

        public void ResetToStart()
        {
            _returnState = SessionShellState.Start;
            Current = SessionShellState.Start;
        }
    }
}
