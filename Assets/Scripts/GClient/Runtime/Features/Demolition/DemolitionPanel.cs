using OperationBlacktide.Client.Content;
using System;
using System.Linq;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Modes.Demolition;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Demolition
{
    public sealed class DemolitionPanel : UIPanel
    {
        [SerializeField] Font font, displayFont;
        [SerializeField] Texture2D mapPreview;
        DemolitionGateway gateway;
        EquipmentCatalog catalog;
        RectTransform sheet, partySlots, roomList, roster, dialogBody;
        GameObject matchPage, browserPage, roomPage, dialog, inviteDialog, createDialog, joinDialog;
        Text status, partyTitle, aiLabel, searchClock, roomHeader, roomCodeLabel, roomAIText;
        Text inviteCode, privateLabel, createAI, roomCount, pageLabel, selectedLabel, centerName;
        Button queue, cancel, reconnect, ready, start, aiButton, matchTab, roomsTab, roomAIToggle, kick, transfer;
        Image aiCheck, roomAICheck, searchBar, matchUnderline, roomsUnderline;
        CanvasGroup lobbyGroup;
        float lobbyAlpha, openedAt, stateAt;
        bool lobbyBlocks, allowAI, roomAI, isPrivate;
        int tab, page;
        string selected="", signature="", partySignature="";
        InputField partyCode, roomCode, joinPassword, password, roomName;
        string User => GameSceneFlow.Instance.Auth.Session.UserId;
        string Username => GameSceneFlow.Instance.Auth.Session.Username;
        Color Faint => new Color(1,1,1,.12f);

        protected override void OnOpen(object args)
        {
            gateway=args as DemolitionGateway??DemolitionGateway.Get();
            catalog=Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
            allowAI=PlayerPrefs.GetInt("Demolition.AllowAI",0)==1;
            var lobby=FindObjectOfType<LobbyPanel>();
            if(lobby!=null)
            {
                lobbyGroup=lobby.GetComponent<CanvasGroup>();if(lobbyGroup==null)lobbyGroup=lobby.gameObject.AddComponent<CanvasGroup>();
                lobbyAlpha=lobbyGroup.alpha;lobbyBlocks=lobbyGroup.blocksRaycasts;lobbyGroup.alpha=0;lobbyGroup.blocksRaycasts=false;
            }
            openedAt=Time.unscaledTime;Build();
            AddEvent(h=>gateway.Changed+=h,h=>gateway.Changed-=h,RefreshState);RefreshState();
        }
        Text Label(Transform p,string text,float x,float y,float w,float h,int size=22,Color? tint=null)
        {var t=DemolitionUI.Label(p,font,text,x,y,w,h,size);t.color=tint??DemolitionUI.White;return t;}
        Text Display(Transform p,string text,float x,float y,float w,float h,int size,Color? tint=null)
        {var t=DemolitionUI.Label(p,displayFont!=null?displayFont:font,text,x,y,w,h,size);t.color=tint??DemolitionUI.White;return t;}
        Button Btn(Transform p,string text,float x,float y,float w,Action action,bool primary=false,float height=44)
            =>DemolitionUI.Button(p,font,text,x,y,w,action,primary,height);
        void Line(Transform p,float x,float y,float w,Color? tint=null)=>DemolitionUI.Fill(p,x,y,w,1,tint??Faint);
        Sprite Portrait(int team=1)=>catalog?.Agent(team==0?"tm_phoenix":"ctm_sas")?.portrait;
        void Build()
        {
            sheet=DemolitionUI.Rect(transform,"Demolition deployment",0,0,1600,900);DemolitionUI.Center(sheet);sheet.gameObject.AddComponent<CanvasGroup>();
            DemolitionUI.Fade(sheet,0,0,670,900,new Color(.02f,.025f,.03f,.97f),Color.clear);
            DemolitionUI.Fade(sheet,1000,0,600,900,Color.clear,new Color(.02f,.025f,.03f,.98f));
            DemolitionUI.Fade(sheet,0,0,1600,150,new Color(.015f,.02f,.025f,.96f),Color.clear,true);
            DemolitionUI.Fade(sheet,0,680,1600,220,Color.clear,new Color(.015f,.02f,.025f,.98f),true);
            Display(sheet,"BLACKTIDE",64,28,220,54,40,DemolitionUI.Accent);
            matchTab=DemolitionUI.TextButton(sheet,font,"快速匹配",524,36,160,()=>SwitchTab(0));
            roomsTab=DemolitionUI.TextButton(sheet,font,"房间大厅",716,36,160,()=>SwitchTab(1));
            matchUnderline=DemolitionUI.Fill(sheet,550,87,108,3,DemolitionUI.Accent);
            roomsUnderline=DemolitionUI.Fill(sheet,742,87,108,3,DemolitionUI.Accent);
            matchUnderline.enabled=roomsUnderline.enabled=false; // The shared tab shader owns selection underlines.
            DemolitionUI.TextButton(sheet,font,"ESC   返回大厅",1324,36,212,()=>UIManager.Instance.Pop(UILayer.Popup));
            Line(sheet,64,110,1472);
            matchPage=DemolitionUI.Rect(sheet,"Matchmaking",0,128,1600,772).gameObject;
            browserPage=DemolitionUI.Rect(sheet,"Room browser",0,128,1600,772).gameObject;
            roomPage=DemolitionUI.Rect(sheet,"Custom room",0,128,1600,772).gameObject;
            BuildMatch();BuildBrowser();BuildRoom();BuildDialogs();
            DemolitionUI.Fit(sheet,0);
        }
        void BuildMatch()
        {
            var p=matchPage.transform;
            Label(p,"爆破模式",64,24,430,64,40);
            MapCard(p,64,122,390,192);
            Label(p,"5v5  ·  先胜 13 回合",64,332,390,30,18,DemolitionUI.Muted);

            partyTitle=Label(p,"我的队伍  1 / 5",1130,40,408,42,28);
            partySlots=DemolitionUI.Rect(p,"Five squad slots",1130,112,406,352);
            Btn(p,"邀请 / 加入队伍",1130,478,258,()=>ShowDialog(inviteDialog));
            Btn(p,"离队",1402,478,134,()=>Send(LobbyAction.LeaveParty));
            aiButton=Btn(p,"",1130,542,406,ToggleAI,false,52);
            aiButton.targetGraphic.color=new Color(1,1,1,.025f);
            aiLabel=aiButton.GetComponentInChildren<Text>();aiLabel.text="允许 AI 补齐";aiLabel.alignment=TextAnchor.MiddleLeft;
            aiLabel.rectTransform.anchoredPosition=new Vector2(56,0);aiLabel.rectTransform.sizeDelta=new Vector2(338,52);
            DemolitionUI.Fill(aiButton.transform,16,16,20,20,new Color(1,1,1,.18f));
            aiCheck=DemolitionUI.Fill(aiButton.transform,20,20,12,12,DemolitionUI.Accent);
            queue=Btn(p,"开始匹配",1130,650,406,Queue,true,76);
            queue.GetComponentInChildren<Text>().fontSize=30;
            searchBar=DemolitionUI.Fill(p,1130,734,0,3,DemolitionUI.Accent);
            cancel=DemolitionUI.TextButton(p,font,"取消匹配",1396,600,140,()=>Send(gateway.State.Phase==LobbyPhase.Confirming?LobbyAction.DeclineMatch:LobbyAction.CancelQueue));
            reconnect=Btn(p,"重连原对局",1130,650,406,()=>gateway.Reconnect(),true,76);
            status=Label(p,"",660,615,400,36,22);status.alignment=TextAnchor.MiddleCenter;
            searchClock=Display(p,"",680,537,360,74,56,DemolitionUI.Accent);searchClock.alignment=TextAnchor.MiddleCenter;
            centerName=Label(p,Username,660,657,400,48,32);centerName.alignment=TextAnchor.MiddleCenter;
            Line(p,765,712,190,DemolitionUI.Accent);
        }
        void MapCard(Transform parent,float x,float y,float w,float h)
        {
            var card=DemolitionUI.Rect(parent,"Dust II deployment",x,y,w,h);
            DemolitionUI.Fill(card,0,0,w,h,new Color32(25,30,32,255));
            if(mapPreview!=null)
            {
                var image=DemolitionUI.Rect(card,"Dust II",0,0,w,h).gameObject.AddComponent<RawImage>();
                image.texture=mapPreview;image.raycastTarget=false;image.color=new Color(.72f,.72f,.72f,1);
            }
            DemolitionUI.Fade(card,0,0,w,h,new Color(0,0,0,.04f),new Color(.02f,.025f,.03f,.92f),true);
            DemolitionUI.Fill(card,0,0,3,h,DemolitionUI.Accent);
            Display(card,"DUST II",18,h-52,w-36,42,30);
        }
        void BuildBrowser()
        {
            var p=browserPage.transform;
            Label(p,"房间大厅",64,24,430,64,40);
            MapCard(p,64,122,390,192);
            Btn(p,"创建房间",64,348,390,()=>ShowDialog(createDialog),true,60);
            Btn(p,"输入房间号",64,420,390,()=>OpenJoin(""),false,44);

            BlackGoldTheme.SurfaceFor(DemolitionUI.Fill(p,510,14,1026,714,DemolitionUI.Ink));
            roomCount=Label(p,"公开房间",542,34,600,42,28);
            Btn(p,"刷新",1400,30,104,()=>Send(LobbyAction.Poll));
            Label(p,"房间名称",546,99,460,28,17,DemolitionUI.Muted);
            Label(p,"人数",1090,99,100,28,17,DemolitionUI.Muted);
            Label(p,"规则",1230,99,200,28,17,DemolitionUI.Muted);
            Line(p,542,137,962);
            roomList=DemolitionUI.Rect(p,"Available rooms",542,151,962,478);
            Btn(p,"上一页",1048,652,138,()=>{page=Mathf.Max(0,page-1);signature="";RefreshState();});
            pageLabel=Label(p,"01 / 01",1200,654,146,40,20,DemolitionUI.Muted);pageLabel.alignment=TextAnchor.MiddleCenter;
            Btn(p,"下一页",1360,652,144,()=>{page++;signature="";RefreshState();});
        }
        void BuildRoom()
        {
            var p=roomPage.transform;
            DemolitionUI.Fade(p,0,0,1600,772,new Color(.02f,.027f,.035f,.74f),new Color(.02f,.027f,.035f,.94f),true);
            roomHeader=Label(p,"",62,49,1140,74,44);
            roomCodeLabel=Label(p,"",64,131,700,32,19,DemolitionUI.Muted);
            Btn(p,"复制房间号",1342,12,194,()=>GUIUtility.systemCopyBuffer=gateway.State.Room?.Code??"");
            Btn(p,"离开房间",1342,66,194,()=>Send(LobbyAction.LeaveRoom));
            Label(p,"进攻方  /  T",64,186,500,46,30,DemolitionUI.Accent);
            Label(p,"防守方  /  CT",892,186,550,46,30,DemolitionUI.Blue);
            Display(p,"VS",748,375,114,84,64,DemolitionUI.Muted);
            Btn(p,"加入 A 队",512,180,196,()=>SelectTeam(0));
            Btn(p,"加入 B 队",1340,180,196,()=>SelectTeam(1));
            roster=DemolitionUI.Rect(p,"Ten player seats",64,241,1472,384);
            selectedLabel=Label(p,"",64,645,590,34,17,DemolitionUI.Muted);
            transfer=Btn(p,"转让房主",64,689,176,()=>gateway.Send(new DemolitionRequest{Action=LobbyAction.TransferHost,Target=selected}));
            kick=Btn(p,"移出成员",254,689,176,()=>gateway.Send(new DemolitionRequest{Action=LobbyAction.Kick,Target=selected}));
            roomAIToggle=Btn(p,"",454,689,276,ToggleRoomAI);
            roomAIText=roomAIToggle.GetComponentInChildren<Text>();
            roomAICheck=DemolitionUI.Fill(roomAIToggle.transform,15,17,10,10,DemolitionUI.Accent);
            ready=Btn(p,"准备",892,664,310,ToggleReady,true,72);
            start=Btn(p,"开始对局",1220,664,316,()=>Send(LobbyAction.StartRoom),true,72);
        }
        void BuildDialogs()
        {
            dialog=DemolitionUI.Rect(sheet,"Deployment dialog",0,0,1600,900).gameObject;
            var blocker=dialog.AddComponent<Image>();blocker.color=new Color(0,0,0,.82f);
            dialogBody=DemolitionUI.Rect(dialog.transform,"Command window",470,215,660,470);
            dialogBody.gameObject.AddComponent<UIAngledGraphic>().color=DemolitionUI.Ink;
            DemolitionUI.Fill(dialogBody,0,0,4,470,DemolitionUI.Accent);
            Btn(dialogBody,"关闭",534,20,102,HideDialog);
            inviteDialog=DemolitionUI.Rect(dialogBody,"Invite",0,0,660,470).gameObject;
            createDialog=DemolitionUI.Rect(dialogBody,"Create",0,0,660,470).gameObject;
            joinDialog=DemolitionUI.Rect(dialogBody,"Join",0,0,660,470).gameObject;
            Label(inviteDialog.transform,"邀请好友",34,24,460,68,40);
            inviteCode=Display(inviteDialog.transform,"------",34,112,560,82,64);
            Btn(inviteDialog.transform,"创建队伍",34,210,276,()=>Send(LobbyAction.CreateParty));
            Btn(inviteDialog.transform,"复制邀请号",330,210,296,()=>GUIUtility.systemCopyBuffer=gateway.State.PartyCode);
            Line(inviteDialog.transform,34,280,592);
            partyCode=DemolitionUI.Input(inviteDialog.transform,font,"输入六位队伍号",34,306,592);
            Btn(inviteDialog.transform,"加入队伍",34,382,592,()=>Send(LobbyAction.JoinParty,partyCode.text),true,58);
            Label(createDialog.transform,"创建房间",34,24,460,68,40);
            roomName=DemolitionUI.Input(createDialog.transform,font,"房间名称",34,112,592);
            password=DemolitionUI.Input(createDialog.transform,font,"房间密码（可选）",34,186,592,true);
            privateLabel=Btn(createDialog.transform,"公开房间",34,270,286,()=>{isPrivate=!isPrivate;RefreshState();},false,52).GetComponentInChildren<Text>();
            createAI=Btn(createDialog.transform,"AI 补齐：关闭",336,270,290,()=>{roomAI=!roomAI;RefreshState();},false,52).GetComponentInChildren<Text>();
            Btn(createDialog.transform,"创建房间",34,382,592,()=>gateway.Send(new DemolitionRequest{Action=LobbyAction.CreateRoom,Name=roomName.text,Password=password.text,IsPrivate=isPrivate,AllowAI=roomAI}),true,58);
            Label(joinDialog.transform,"加入房间",34,24,460,68,40);
            roomCode=DemolitionUI.Input(joinDialog.transform,font,"输入六位房间号",34,112,592);
            joinPassword=DemolitionUI.Input(joinDialog.transform,font,"房间密码（可选）",34,200,592,true);
            Btn(joinDialog.transform,"加入房间",34,382,592,()=>gateway.Send(new DemolitionRequest{Action=LobbyAction.JoinRoom,Code=roomCode.text,Password=joinPassword.text}),true,58);
            // Keep close above the content, so it remains clickable on every dialog page.
            dialogBody.GetChild(1).SetAsLastSibling();
            dialog.SetActive(false);
        }
        void ShowDialog(GameObject pageObject)
        {
            dialog.SetActive(true);inviteDialog.SetActive(pageObject==inviteDialog);createDialog.SetActive(pageObject==createDialog);joinDialog.SetActive(pageObject==joinDialog);
        }
        void HideDialog()=>dialog.SetActive(false);
        void OpenJoin(string code){roomCode.text=code;joinPassword.text="";ShowDialog(joinDialog);}
        void SwitchTab(int value){tab=value;HideDialog();RefreshState();}
        void Send(LobbyAction action,string code="")=>gateway.Send(new DemolitionRequest{Action=action,Code=code});
        void Queue()=>gateway.Send(new DemolitionRequest{Action=gateway.State.Phase==LobbyPhase.Confirming?LobbyAction.AcceptMatch:LobbyAction.Queue,AllowAI=allowAI});
        void ToggleAI()
        {
            if(gateway.State.Phase!=LobbyPhase.Idle)return;
            allowAI=!allowAI;PlayerPrefs.SetInt("Demolition.AllowAI",allowAI?1:0);PlayerPrefs.Save();RefreshState();
        }
        void ToggleRoomAI()
        {
            var r=gateway.State.Room;if(r==null)return;
            gateway.Send(new DemolitionRequest{Action=LobbyAction.ConfigureRoom,Name=r.Name,IsPrivate=r.IsPrivate,AllowAI=!r.AllowAI});
        }
        void ToggleReady()=>gateway.Send(new DemolitionRequest{Action=LobbyAction.Ready,Ready=!(gateway.State.Room?.Members.FirstOrDefault(m=>m.UserId==User)?.Ready??false)});
        void SelectTeam(int team)=>gateway.Send(new DemolitionRequest{Action=LobbyAction.SelectTeam,Team=team});
        void RefreshState()
        {
            if(sheet==null)return;stateAt=gateway.StateAt;var s=gateway.State;bool inRoom=s.Room!=null;
            bool queued=s.Phase==LobbyPhase.Queued, confirming=s.Phase==LobbyPhase.Confirming;
            if(queued||confirming||s.Match!=null)tab=0;
            matchPage.SetActive(!inRoom&&tab==0);browserPage.SetActive(!inRoom&&tab==1);roomPage.SetActive(inRoom);
            matchUnderline.gameObject.SetActive(!inRoom&&tab==0);roomsUnderline.gameObject.SetActive(!inRoom&&tab==1);
            BlackGoldTheme.Select(matchTab,!inRoom&&tab==0);BlackGoldTheme.Select(roomsTab,!inRoom&&tab==1);
            matchTab.interactable=!inRoom;roomsTab.interactable=!inRoom&&s.Phase==LobbyPhase.Idle&&s.Match==null;
            aiCheck.gameObject.SetActive(queued||confirming?s.AllowAI:allowAI);
            aiButton.interactable=!gateway.Busy&&s.Phase==LobbyPhase.Idle&&(s.LeaderId.Length==0||s.LeaderId==User);
            queue.gameObject.SetActive(s.Match==null);reconnect.gameObject.SetActive(s.Match!=null);
            queue.interactable=!gateway.Busy&&(s.Phase==LobbyPhase.Idle&&(s.LeaderId.Length==0||s.LeaderId==User)||confirming&&!s.Confirmed);
            queue.GetComponentInChildren<Text>().text=gateway.Busy?"正在提交…":confirming?(s.Confirmed?"等待队友确认":"确认对局"):queued?"正在匹配":"开始匹配";
            cancel.gameObject.SetActive(queued||confirming);
            centerName.text=Username;
            privateLabel.text=isPrivate?"私密房间":"公开房间";createAI.text=roomAI?"AI 补齐：开启":"AI 补齐：关闭";
            inviteCode.text=s.PartyCode.Length==0?"------":s.PartyCode;
            var newParty=JsonUtility.ToJson(new MemberCollection{Members=s.Party})+s.LeaderId;
            if(newParty!=partySignature){partySignature=newParty;DrawParty(s);}
            if(inRoom)HideDialog();
            string next=JsonUtility.ToJson(s.Room)+JsonUtility.ToJson(new RoomCollection{Rooms=s.Rooms})+page+selected;
            if(next==signature)return;signature=next;DrawBrowser(s);
            if(!inRoom)return;
            var room=s.Room;
            roomHeader.text=room.Name;
            roomCodeLabel.text="# "+room.Code+"    /    "+(room.IsPrivate?"私密房间":"公开房间")+(room.StartsIn>=0?"    /    "+Mathf.CeilToInt(room.StartsIn)+" 秒后开始":"");
            bool host=room.HostId==User;
            bool allReady=room.Members.All(m=>m.Ready&&m.Online);
            ready.interactable=!room.Started;
            ready.GetComponentInChildren<Text>().text=room.Members.FirstOrDefault(m=>m.UserId==User)?.Ready==true?"取消准备":"准备";
            start.interactable=host&&!room.Started&&room.StartsIn<0&&allReady&&(room.AllowAI||room.Members.Length==10);
            roomAIToggle.interactable=host&&!room.Started;roomAIText.text=room.AllowAI?"AI 补齐：开启":"AI 补齐：关闭";
            roomAICheck.gameObject.SetActive(room.AllowAI);
            bool canManage=host&&selected.Length>0&&selected!=User&&room.Members.Any(m=>m.UserId==selected);
            kick.interactable=transfer.interactable=canManage;
            selectedLabel.text=selected.Length>0?"已选择  "+(room.Members.FirstOrDefault(m=>m.UserId==selected)?.Name??""):"";
            Clear(roster);
            for(int team=0;team<2;team++)
            {
                var members=room.Members.Where(m=>m.Team==team).ToArray();
                for(int slot=0;slot<5;slot++)
                {
                    var m=slot<members.Length?members[slot]:null;
                    DrawSeat(roster,m,team*828,slot*77,644,67,team,m?.UserId==room.HostId,room.AllowAI,slot);
                }
            }
        }
        void DrawParty(DemolitionResponse state)
        {
            Clear(partySlots);var members=state.Party;
            if(members.Length==0)members=new[]{new LobbyMember{UserId=User,Name=Username,Online=true}};
            partyTitle.text="我的队伍   "+members.Length+" / 5";
            for(int i=0;i<5;i++)DrawSeat(partySlots,i<members.Length?members[i]:null,0,i*70,406,62,1,i<members.Length&&(state.LeaderId.Length==0||members[i].UserId==state.LeaderId),false,i,true);
        }
        void DrawSeat(Transform parent,LobbyMember member,float x,float y,float w,float h,int team,bool leader,bool bots,int slot,bool party=false)
        {
            string id=member?.UserId??"";
            var row=Btn(parent,"",x,y,w,()=>{if(party)ShowDialog(inviteDialog);else{selected=id;signature="";RefreshState();}},false,h);
            var color=team==0?DemolitionUI.Accent:DemolitionUI.Blue;
            row.targetGraphic.color=member==null?new Color(.05f,.065f,.075f,.36f):id==selected?new Color(.22f,.26f,.28f,.95f):new Color(.075f,.095f,.11f,.9f);
            BlackGoldTheme.Select(row,id.Length>0&&id==selected);
            row.GetComponentInChildren<Text>().gameObject.SetActive(false);
            DemolitionUI.Fill(row.transform,0,0,member!=null?3:1,h,member!=null?color:new Color(1,1,1,.08f));
            if(member!=null)
            {
                DemolitionUI.Icon(row.transform,Portrait(team),7,1,h-2,h-2);
                Label(row.transform,member.Name,h+15,10,w-h-100,30,21);
                if(leader)Label(row.transform,party?"队长":"房主",h+15,39,200,23,14,DemolitionUI.Muted);
                var tag=Label(row.transform,member.Online?(party?"":member.Ready?"已准备":"未准备"):"离线",w-100,17,86,34,16,member.Ready||party?color:DemolitionUI.Muted);
                tag.alignment=TextAnchor.MiddleRight;
            }
            else
            {
                Display(row.transform,"+",20,4,46,54,38,new Color(1,1,1,.25f));
                Label(row.transform,party?"邀请队友":bots?"AI 待补位":"等待玩家",h+15,17,w-h-25,36,18,DemolitionUI.Muted);
            }
        }
        void DrawBrowser(DemolitionResponse state)
        {
            Clear(roomList);int max=Mathf.Max(0,(state.Rooms.Length-1)/5);page=Mathf.Clamp(page,0,max);
            roomCount.text="公开房间  /  "+state.Rooms.Length;pageLabel.text=(page+1).ToString("00")+" / "+(max+1).ToString("00");
            if(state.Rooms.Length==0)
            {
                Label(roomList,"暂无公开房间",44,212,860,46,30);
            }
            foreach(var entry in state.Rooms.Skip(page*5).Take(5).Select((r,i)=>(r,i)))
            {
                var r=entry.r;
                var row=Btn(roomList,"",0,entry.i*88,962,()=>{if(r.HasPassword)OpenJoin(r.Code);else Send(LobbyAction.JoinRoom,r.Code);},false,76);
                row.GetComponentInChildren<Text>().gameObject.SetActive(false);
                Label(row.transform,r.Name,18,8,514,36,24);
                Label(row.transform,"#"+r.Code+"   /   DUST II"+(r.HasPassword?"   /   密码房":""),18,46,514,25,15,DemolitionUI.Muted);
                Display(row.transform,r.Members.Length+" / 10",548,15,138,48,31);
                Label(row.transform,r.AllowAI?"允许 AI":"仅限真人",688,22,160,32,18,r.AllowAI?DemolitionUI.Accent:DemolitionUI.Muted);
                Display(row.transform,">",900,14,50,48,32,DemolitionUI.Accent);
            }
        }
        static void Clear(Transform target){foreach(Transform child in target){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
        [Serializable] sealed class RoomCollection{public RoomView[] Rooms;}
        [Serializable] sealed class MemberCollection{public LobbyMember[] Members;}
        void Update()
        {
            if(sheet==null)return;DemolitionUI.Fit(sheet,0);var s=gateway.State;
            float elapsed=s.WaitSeconds+Mathf.Max(0,Time.unscaledTime-stateAt);
            bool queued=s.Phase==LobbyPhase.Queued,confirming=s.Phase==LobbyPhase.Confirming;
            status.text=gateway.Error.Length>0?gateway.Error:gateway.Busy?"连接中…":queued?"匹配中":confirming?"已找到对局":s.Match!=null?"对局进行中":s.Phase==LobbyPhase.Allocating?"进入对局中":"";
            searchClock.text=gateway.Busy?Mathf.FloorToInt(gateway.RequestSeconds).ToString("00")+"s":queued?(s.AllowAI?Mathf.CeilToInt(Mathf.Max(0,30-elapsed)).ToString("00")+"s":TimeSpan.FromSeconds(elapsed).ToString(@"mm\:ss")):confirming?Mathf.CeilToInt(Mathf.Max(0,s.ConfirmSeconds-(Time.unscaledTime-stateAt))).ToString("00")+"s":"";
            searchBar.rectTransform.sizeDelta=new Vector2(queued?406*(s.AllowAI?Mathf.Clamp01(elapsed/30):Mathf.PingPong(Time.unscaledTime*.16f,1)):0,3);
            sheet.GetComponent<CanvasGroup>().alpha=Mathf.Clamp01((Time.unscaledTime-openedAt)*5);
        }
        protected override void OnClose()
        {
            if(gateway!=null&&(gateway.State.Phase==LobbyPhase.Queued||gateway.Busy&&gateway.PendingAction==LobbyAction.Queue))Send(LobbyAction.CancelQueue);
            if(gateway!=null&&gateway.State.Phase==LobbyPhase.Confirming)Send(LobbyAction.DeclineMatch);
            if(lobbyGroup!=null){lobbyGroup.alpha=lobbyAlpha;lobbyGroup.blocksRaycasts=lobbyBlocks;lobbyGroup=null;}
            if(sheet!=null)Destroy(sheet.gameObject);sheet=null;signature="";partySignature="";selected="";tab=0;
        }
    }
}

