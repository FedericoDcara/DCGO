using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CPlayerElement : MonoBehaviour
{
    //For Room Information UI display
    public Text PlayerName;   //PlayerName
    public Text IsReady; //standby state

    //For storing the roomname of the room entry button
    private string playername;

    //Function to set Room information from GetRoomList to RoomElement
    public void SetPlayerInfo(string _PlayerName, bool _IsReady)
    {
        if (_IsReady)
        {
            SetPlayerInfo(
                _PlayerName,
                LocalizeUtility.GetLocalizedString(EngMessage: "Ready", JpnMessage: "準備完了"),
                new Color32(53, 255, 4, 255));
        }
        else
        {
            SetPlayerInfo(
                _PlayerName,
                LocalizeUtility.GetLocalizedString(EngMessage: "Not Ready", JpnMessage: "準備中"),
                Color.red);
        }
    }

    public void SetPlayerInfo(string _PlayerName, string statusText, Color statusColor)
    {
        playername = _PlayerName;
        if (PlayerName != null)
        {
            PlayerName.text = _PlayerName;
        }

        if (IsReady != null)
        {
            IsReady.text = statusText;
            IsReady.color = statusColor;
        }
    }
}
