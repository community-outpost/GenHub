using GenHub.Core.Models.GameProfile;
using Xunit;

namespace GenHub.Tests.Core.Models.GameProfile
{
    /// <summary>
    /// Tests for <see cref="LaunchProgress"/>.
    /// </summary>
    public class LaunchProgressTests
    {
        /// <summary>
        /// Tests that LaunchProgress can be created with default values.
        /// </summary>
        [Fact]
        public void LaunchProgress_DefaultConstruction_ShouldHaveDefaultValues()
        {
            // Act
            var progress = new LaunchProgress();

            // Assert
            Assert.Equal(LaunchPhase.ValidatingProfile, progress.Phase);
            Assert.Equal(0, progress.PercentComplete);
        }

        /// <summary>
        /// Tests that LaunchProgress properties can be set.
        /// </summary>
        [Fact]
        public void LaunchProgress_PropertyAssignment_ShouldWork()
        {
            // Arrange
            var progress = new LaunchProgress();

            // Act
            progress.Phase = LaunchPhase.Starting;
            progress.PercentComplete = 75;

            // Assert
            Assert.Equal(LaunchPhase.Starting, progress.Phase);
            Assert.Equal(75, progress.PercentComplete);
        }

        /// <summary>
        /// Tests that LaunchProgress can handle all phase transitions.
        /// </summary>
        [Fact]
        public void LaunchProgress_AllPhaseTransitions_ShouldWork()
        {
            // Arrange
            var progress = new LaunchProgress();
            var phases = new[]
            {
                LaunchPhase.ValidatingProfile,
                LaunchPhase.PreparingWorkspace,
                LaunchPhase.ResolvingContent,
                LaunchPhase.Starting,
                LaunchPhase.Running,
                LaunchPhase.Completed,
                LaunchPhase.Failed,
            };

            // Act & Assert
            foreach (var phase in phases)
            {
                progress.Phase = phase;
                Assert.Equal(phase, progress.Phase);
            }
        }

        /// <summary>
        /// Tests that PercentComplete accepts valid range values.
        /// </summary>
        /// <param name="percent">The percent value to test.</param>
        [Theory]
        [InlineData(0)]
        [InlineData(25)]
        [InlineData(50)]
        [InlineData(75)]
        [InlineData(100)]
        public void LaunchProgress_ValidPercentComplete_ShouldAccept(int percent)
        {
            // Arrange
            var progress = new LaunchProgress();

            // Act
            progress.PercentComplete = percent;

            // Assert
            Assert.Equal(percent, progress.PercentComplete);
        }

        /// <summary>
        /// Tests that LaunchProgress can be constructed with specific values.
        /// </summary>
        [Fact]
        public void LaunchProgress_ConstructorWithValues_ShouldSetProperties()
        {
            // Arrange & Act
            var progress = new LaunchProgress
            {
                Phase = LaunchPhase.ResolvingContent,
                PercentComplete = 45,
            };

            // Assert
            Assert.Equal(LaunchPhase.ResolvingContent, progress.Phase);
            Assert.Equal(45, progress.PercentComplete);
        }

        /// <summary>
        /// Tests that LaunchProgress rejects invalid percent values.
        /// </summary>
        /// <param name="percent">The percent value to test.</param>
        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void LaunchProgress_InvalidPercentValues_ShouldThrowArgumentOutOfRangeException(int percent)
        {
            // Arrange
            var progress = new LaunchProgress();

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => progress.PercentComplete = percent);
            Assert.Equal("value", exception.ParamName);
            Assert.Contains("Percentage must be between 0 and 100", exception.Message);
        }

        /// <summary>
        /// Tests that LaunchProgress can be reset to initial state.
        /// </summary>
        [Fact]
        public void LaunchProgress_Reset_ShouldReturnToInitialState()
        {
            // Arrange
            var progress = new LaunchProgress
            {
                Phase = LaunchPhase.Running,
                PercentComplete = 80,
            };

            // Act
            progress.Phase = LaunchPhase.ValidatingProfile;
            progress.PercentComplete = 0;

            // Assert
            Assert.Equal(LaunchPhase.ValidatingProfile, progress.Phase);
            Assert.Equal(0, progress.PercentComplete);
        }
    }
}
