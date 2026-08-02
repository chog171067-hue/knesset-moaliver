using KioskApp.Core;
using KioskApp.Core.Models;
using Xunit;

namespace KioskApp.Core.Tests
{
    public class KioskSessionTests
    {
        [Fact]
        public void Start_SetsCurrentUserAndIsActive()
        {
            var session = new KioskSession();
            var user = new KioskUser { Id = "u1", Username = "dani", Balance = 10 };
            var config = new KioskConfig { PricePerPage = 0.2m };

            session.Start(user, config);

            Assert.True(session.IsActive);
            Assert.Equal("u1", session.CurrentUser!.Id);
            Assert.Equal(0.2m, session.Config!.PricePerPage);
        }

        [Fact]
        public void UpdateBalance_MutatesUserAndRaisesEvent()
        {
            var session = new KioskSession();
            session.Start(new KioskUser { Id = "u1", Balance = 10 }, new KioskConfig());

            decimal? raised = null;
            session.BalanceChanged += (_, newBalance) => raised = newBalance;

            session.UpdateBalance(7.5m);

            Assert.Equal(7.5m, session.CurrentUser!.Balance);
            Assert.Equal(7.5m, raised);
        }

        [Fact]
        public void UpdateBalance_BeforeStart_DoesNothing()
        {
            var session = new KioskSession();
            var raised = false;
            session.BalanceChanged += (_, _) => raised = true;

            session.UpdateBalance(100m);

            Assert.False(raised);
            Assert.False(session.IsActive);
        }

        [Fact]
        public void End_ClearsUserAndRaisesSessionEnded()
        {
            var session = new KioskSession();
            session.Start(new KioskUser { Id = "u1" }, new KioskConfig());

            var ended = false;
            session.SessionEnded += (_, _) => ended = true;

            session.End();

            Assert.False(session.IsActive);
            Assert.Null(session.CurrentUser);
            Assert.True(ended);
        }
    }
}
