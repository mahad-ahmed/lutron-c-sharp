# lutron-c-sharp
A client library to control Lutron Homeworks QS devices


## Usage:
### Set up
Note: This is a singleton implementation and the username and password are set at compile time.
</br></br>
Change <YOUR_USERNAME> and <YOUR_PASSWORD> to your system's username and password respectively:
```
private readonly byte[] USERNAME = Encoding.UTF8.GetBytes("<YOUR_USERNAME>\r\n");
private readonly byte[] PASSWORD = Encoding.UTF8.GetBytes("<YOUR_PASSWORD>\r\n");
```


</br>


### Add a Connection State Listener
`LutronClient.Instance().ConnectionStateListener = this;`


</br>


### Add a Level Change Listener
`LutronClient.Instance().AddOnLevelChangeListener(this);`


</br>


### Start the connection
`LutronClient.Instance().Connect();`


</br>


### Let there be light
`LutronClient.Instance().LedOn(<INTEGRATION_ID>);`


</br>


### Change the level of a device
Note: Uses a timed-out queue
`LutronClient.Instance().QueueLevel(<INTEGRATION_ID>, slider.Value);`
