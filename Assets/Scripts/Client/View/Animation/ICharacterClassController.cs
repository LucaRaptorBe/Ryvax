using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Interface for character class-specific animation controllers.
    /// Each class implements this to handle its unique abilities and animations.
    /// </summary>
    public interface ICharacterClassController
    {
        /// <summary>
        /// The character class this controller handles.
        /// </summary>
        CharacterClassType Class { get; }

        /// <summary>
        /// Initialize the controller with an Animator reference.
        /// Called once when the PlayerView is initialized.
        /// </summary>
        /// <param name="animator">The Animator component to control</param>
        void Initialize(Animator animator);

        /// <summary>
        /// Update animations every frame.
        /// Called from PlayerView.UpdateAnimation().
        /// </summary>
        void UpdateAnimations();

        /// <summary>
        /// Called when this class layer is activated.
        /// Use this to initialize state or trigger entry animations.
        /// </summary>
        void OnActivated();

        /// <summary>
        /// Called when this class layer is deactivated (switching to another class).
        /// Use this to clean up state and reset parameters.
        /// </summary>
        void OnDeactivated();
    }
}
