using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityWebBrowser.Helper;
using UnityWebBrowser.Input;
using UnityWebBrowser.Shared;
using UnityWebBrowser.Shared.Events;

namespace UnityWebBrowser.Core
{
    /// <summary>
    ///     Input handler for <see cref="RawImageUwbClientManager"/>.
    /// </summary>
    public abstract class RawImageUwbClientInputHandler : RawImageUwbClientManager, 
        IPointerEnterHandler, 
        IPointerExitHandler, 
        IPointerDownHandler,
        IPointerUpHandler
    {
        /// <summary>
        ///     The <see cref="WebBrowserInputHandler"/> to use
        /// </summary>
        [Tooltip("The input handler to use")]
        public WebBrowserInputHandler inputHandler;
        
        private Coroutine keyboardAndMouseHandlerCoroutine;
        private Vector2 lastSuccessfulMousePositionSent;

        protected override void OnStart()
        {
            base.OnStart();
            if (inputHandler == null)
                throw new NullReferenceException("The input handler is null! You need to assign it in the editor!");
        }

        protected override void OnDestroyed()
        {
            base.OnDestroyed();
            StopKeyboardAndMouseHandler();
        }
        
        /// <summary>
        ///     Gets the current mouse position on the image
        /// </summary>
        /// <param name="pos"></param>
        /// <returns>Returns true if the mouse is in the image.</returns>
        public bool GetMousePosition(out Vector2 pos)
        {
            Vector2 mousePos = inputHandler.GetCursorPos();

            if (WebBrowserUtils.GetScreenPointToLocalPositionDeltaOnImage(image, mousePos, out pos))
            {
                Texture imageTexture = image.texture;
                pos.x *= imageTexture.width;
                pos.y *= imageTexture.height;

                return true;
            }

            return false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (browserClient is {IsConnected: false})
                return;

            keyboardAndMouseHandlerCoroutine = StartCoroutine(KeyboardAndMouseHandler());
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            StopKeyboardAndMouseHandler();
        }

        private void StopKeyboardAndMouseHandler()
        {
            if(keyboardAndMouseHandlerCoroutine != null)
            {
                StopCoroutine(keyboardAndMouseHandlerCoroutine);
                inputHandler.OnStop();
            }
        }

        private IEnumerator KeyboardAndMouseHandler()
        {
            inputHandler.OnStart();

            while (Application.isPlaying)
            {
                if(!browserClient.IsConnected)
                    continue;
                
                if (GetMousePosition(out Vector2 pos))
                {
                    //Mouse position
                    if (lastSuccessfulMousePositionSent != pos)
                    {
                        browserClient.SendMouseMove(pos);
                        lastSuccessfulMousePositionSent = pos;
                    }

                    //Mouse scroll
                    float scroll = inputHandler.GetScroll();
                    scroll *= browserClient.BrowserTexture.height;

                    if (scroll != 0)
                        browserClient.SendMouseScroll(pos, (int) scroll);
                    
                    //Input
                    WindowsKey[] keysDown = inputHandler.GetDownKeys();
                    WindowsKey[] keysUp = inputHandler.GetUpKeys();
                    string inputBuffer = inputHandler.GetFrameInputBuffer();
                    
                    if(keysDown.Length > 0 || keysUp.Length > 0 || inputBuffer.Length > 0)
                        browserClient.SendKeyboardControls(keysDown, keysUp, inputBuffer);
                }
                
                yield return 0;
            }
            
            inputHandler.OnStop();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            MouseClickType clickType = eventData.button switch
            {
                PointerEventData.InputButton.Left => MouseClickType.Left,
                PointerEventData.InputButton.Right => MouseClickType.Right,
                PointerEventData.InputButton.Middle => MouseClickType.Middle,
                _ => throw new ArgumentOutOfRangeException()
            };

            if (GetMousePosition(out Vector2 pos))
                browserClient.SendMouseClick(pos, eventData.clickCount, clickType, MouseEventType.Down);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            MouseClickType clickType = eventData.button switch
            {
                PointerEventData.InputButton.Left => MouseClickType.Left,
                PointerEventData.InputButton.Right => MouseClickType.Right,
                PointerEventData.InputButton.Middle => MouseClickType.Middle,
                _ => throw new ArgumentOutOfRangeException()
            };

            if (GetMousePosition(out Vector2 pos))
                browserClient.SendMouseClick(pos, eventData.clickCount, clickType, MouseEventType.Up);
        }
    }
}