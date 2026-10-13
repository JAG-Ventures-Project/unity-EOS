using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using UnityEngine;
using UnityEngine.Assertions;

public class AuthManager : MonoBehaviour
{
    // TODO :
    // guest login
        // use EGS login with guest account 
        // uses the device PUID 
            // how to do it : 
                // create a device id if it doesn't exist 
                // get the device PUID 
                // use it to login through connect.login 
             
    // epic account login
        // epic will be open in the background get the epic id in the callback 
            // how to do it : 
                // connect using platforminterface , connectinterface and authmanager, all in the connect manager
                // get user data 
                // use it to login through connect.login 
            
    // steam login
        // use EGS login with steam account 
        // steam will be open in the background get the steam session id in the callback 
}