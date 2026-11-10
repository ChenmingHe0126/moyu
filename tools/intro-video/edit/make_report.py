"""Builds the storyboard report page from the rendered video: frames are pulled at each shot's time and embedded,
and a smaller copy of the video is published next to the page. Output: dist/intro-video/report/
Run: python tools/intro-video/edit/make_report.py"""
import base64, html, io, json, subprocess
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
VIDEO = ROOT / "dist/intro-video/out/moyu-intro.mp4"
OUT = ROOT / "dist/intro-video/report"
BEAT = 60 / 94


def frame(t, width=360):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{t:.3f}", "-i", str(VIDEO), "-frames:v", "1", "-vf", f"scale={width}:-2",
                          "-f", "image2pipe", "-vcodec", "png", "-"], capture_output=True, check=True).stdout
    im = Image.open(io.BytesIO(raw)).convert("RGB")
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=80)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


# (beat from, beat to, section, title, caption, what, why, pitfall, still time (s) or None for the middle)
SHOTS = [
    (0, 5, "game", "钩子：打砖块", "上班时间 / 我在 Rhino 里打砖块", "Rebound 第 0 帧就是六个球的混战，1.5 秒吃到「穿透」，一路打穿砖墙。",
     "信息流里前 3 秒决定去留：先给最热闹的画面，再交代是什么。", "画面放大到球场占宽度约 70%：再大就会顶到游戏自己的分数 HUD。", 2.25),
    (5, 10, "boss", "老板键：幕墙立面", "老板来了？/ 一帧变成幕墙立面", "按 ` 的那一帧，砖墙变成 24 层幕墙塔楼立面：轴网、标高、1:50 典型开间节点、指标卡。玻璃板块按当时砖块的残缺取样。",
     "招牌功能放在第 3 秒，方向是游戏→图纸（快的那个方向）。", "伪装图原本按横屏构图，竖屏会切掉节点标注、指标卡压住塔顶；录制时切换后立刻把相机右移 35 m、换 77 mm 镜头重新取景。", 4.7),
    (10, 15, "title", "片名", "Moyu 摸鱼 / 一半拓扑优化 · 一半摸鱼", "图标 + 名称，背景是虚化的立面，底行写清 Grasshopper 插件、Rhino 8 Windows、免费开源。",
     "第 6 秒就交代清楚「这是什么」：评审都要求定义句尽早出现。", "Mac 用户装不了，所以 Windows 写在片名卡上，不只在片尾小字里。", None),
    (15, 21, "title", "四个面板", "四个面板 / 名字都很正经", "Grasshopper 的 Moyu 标签栏（本机高清截取），推近到 3–6 号面板，逐个对应叠楼、蜿蜒、回弹、雨燕。",
     "让懂 GH 的人一眼看到这是真实的插件面板，而且伪装从面板名就开始了。", "面板名按 1.0.1 的真实编号（3 | Massing …），不是后期编的。", 12.46),
    (21, 29, "real", "实机：点 Run", "真的在 Rhino 里 / Grasshopper 里点 ▶ Run 就开玩", "Rhino 8 和 Grasshopper 窗口的实机画面：四个视口里是示例文件里用 Rhino 建模的门楼方块；光标点 Massing Study 电池下方的 ▶ Run，Perspective 视口最大化、真实的 3-2-1 后开局，机器人连着落几块。",
     "全片其他镜头太好看，看不出是在 Rhino 里；这一段把 Rhino 菜单、工具栏、命令行、GH 画布原样放出来。", "两个窗口逐帧截取（PrintWindow），不需要在屏幕最前面；标题栏里的许可证类型裁掉了。光标是后期画的，位置取自 Run 按钮的真实坐标；倒数中间剪掉了 2 秒。", 16.85),
    (29, 34, "boss", "实机：老板键", "老板键一按 / 视口、命令行、电池一起伪装", "按 ` 的同一帧：视口变成体块研究，Rhino 命令行打出「MassingStudy: Option B updated · 12F · GFA … · FAR …」，Grasshopper 里 Metrics 面板同时变成 Option B 和容积率等指标。",
     "老板键不只是换画面：命令行和电池输出也一起「交作业」，这是只有在真 Rhino 里才看得到的细节。", "两个珊瑚色框把命令行和 Metrics 面板标出来，否则在手机上找不到。", 20.32),
    (34, 41, "game", "叠楼：自定义方块 + 双消", "方块能自己建模 / Gumball 转一下，一次消两层", "拉近看叠楼：自定义「门楼」方块正在下落；Gumball 蓝环亮起、拖动、方块绕竖轴转 90°；落下消一层（+840）；接着立起的点式方块补进前排两格竖井，DOUBLE FLOOR +1,400。",
     "建筑师最在意的点是「方块可以自己建模」，正好接上一镜 Rhino 里的门楼方块。", "叠楼的老板键已经在实机段演过，这里只演玩法。重录时把机位压低，井底高于字幕区。", 25.53),
    (41, 46, "game", "蜿蜒：圆环面", "什么曲面都能爬 / 圆环、管子、地形、球面", "Site Surface 生成的圆环面全景，蛇绕着环吃果子，连击 ×5，西瓜 +400。",
     "「什么曲面都能爬」只有圆环这样的闭合曲面才看得出来。", "圆环上的第一人称相机贴得太近、看不出形状，圆环的老板画面也更像坡度图，所以圆环只拍全景。", 28.6),
    (46, 50, "game", "蜿蜒：第一人称", "第一人称视角 / 连吃有连击，最高 ×5", "起伏地形上的第一人称，路线在场地中央绕方形，前方一直有果子。",
     "第一人称是蜿蜒独有的玩法。", "路线贴边时会拍到地形的侧壁，改成绕场地中央。升级卡片在竖屏下会压到状态行，录制时锁住了等级。", 30.64),
    (50, 55, "boss", "老板键：景观平面", "一帧变成 景观流线图", "第一人称一帧切成景观平面：等高线坡度色、树、观景台 N1 / N2、路线 A、指标卡。",
     "从第一人称直接切，反差最大。", "竖屏下两侧有裁切，读起来像局部放大的平面，可以接受。", 33.7),
    (55, 60, "game", "雨燕", "只用一个键：空格 / 扇翅膀，穿过柱廊", "Exposed 难度、预飞 14 秒后的画面：一秒过一道墙，4.5 秒出「LEVEL 02」卡片。机器人是仓库测试里的同一个飞行员。",
     "雨燕默认太慢（5 秒只过两道墙），换成 Exposed 难度并预飞。", "横向卷轴在竖屏里只占中间一条，相机拉近到大厅约占 45% 高度。", 36.77),
    (60, 64, "boss", "老板键：剖面", "一帧变成 采光通风剖面", "一帧切成住宅剖面：采光系数色条、冬夏太阳角、气流线、P1 风速探头、指标卡。",
     "第四张图纸，说明每个游戏的伪装都不一样。", "竖屏里只看得到中间三个开间；指标卡标题被顶边裁掉一行（游戏 HUD 的横屏布局）。", 39.83),
    (64, 72, "back", "再按一次：3-2-1", "老板走了？再按一次 / 3-2-1，回到游戏", "回到游戏前是真实的 3 秒倒数，不加速；倒数中弹出「Alt+Tab 切走也会自动伪装」「默认静音」两个小标签。",
     "评审指出过：倒数是 3 秒，剪辑不能把它说快。", "这一段占 5 秒，靠倒数本身的节拍撑住。", 42.89),
    (72, 78, "topo", "2D 拓扑优化", "老板真来检查？/ 那就真算个拓扑优化", "150×175 像素设计域，底部中间支座、顶部实心板上的均布荷载、中间一个圆洞。75 次真实迭代长成一棵「树」，左上角标 ×21 倍速（迭代段按 0.8 倍放慢）。",
     "顺着老板键的梗往下接：老板真来查岗，那就真算一个。另一半是正经工具，但不用说教的口吻讲。", "迭代是后台先解好、逐帧显示真实的第 n 步；倍速按本机实测求解时间算。", 48.61),
    (78, 86, "topo", "3D 拓扑优化", "3D 体素也行 / 收敛后直接出光滑网格", "40×40×44 体素的桌子：顶板受压、四角铰支。37 次迭代从实心块变成四条腿，镜头缓慢环绕，3.4 秒换成 Visualize 3D 的光滑等值面。×8 倍速。",
     "全片最「正经」也最好看的一镜，给足 5 秒。", "体素阶段和光滑网格都用插件自己的网格生成代码，颜色是 Visualize 3D 默认色。", 54.13),
    (86, 91, "info", "怎么做出来的", "说出来你可能不信：代码几乎全是 AI 写的", "三个勾（代码、测试、文档）+ 计时数字滚到 9 小时 48 分，最后一行「这期间我主要负责提需求，和摸鱼」。",
     "AI 开发是事实但不是卖点，给 3 秒，够读完。", "用时写明「1.0.0 版 · Claude Code 开发用时」，不笼统说「AI 写的」。", 57.7),
    (91, 95, "info", "安装", "PackageManager / 搜 moyu，点 Install", "Rhino 包管理器里搜 moyu 的真实录屏（11-08）。顶上三个小标签：Rhino 8、Windows、免费。",
     "只给一个动作：搜 moyu。", "这一镜复用旧录屏，清晰度比新录的镜头低。", 59.74),
    (95, 99, "info", "示例", "Examples 电池 / 一键打开现成示例", "Examples 电池的六个案例按钮（中英双语）。",
     "GH 用户说「电池」，比「组件」更像自己人。", "同样是旧录屏。", 62.3),
    (99, 104, "title", "片尾", "祝摸鱼愉快 / 老板路过？按 ` 就好", "图标、「祝摸鱼愉快」、一行小字「老板路过？按 ` 就好」、搜索提示、免费开源、配乐署名（CC BY 4.0 要求署名）。",
     "不说教，顺着摸鱼的梗收尾；最后停在「搜 moyu」，让人记住怎么装。", "署名同时写进发布简介。", 64.0),
]

SECTION = {"real": "实机", "game": "玩法", "boss": "老板键", "title": "交代", "back": "回游戏", "topo": "拓扑优化", "info": "信息"}

SCORES = {  # judge lens -> draft -> [hook, clarity, pacing, highlights, honesty]
    "短视频剪辑": {"反转": [7, 8, 8, 8, 7], "高光": [9, 7, 6, 7, 7], "故事": [5, 8, 6, 6, 9]},
    "建筑师观众": {"反转": [7.5, 7, 8.5, 8.5, 7.5], "高光": [9, 8, 6, 7, 7], "故事": [5.5, 8, 6, 7, 8.5]},
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    # A lighter copy of the video for the page (the full-quality file is docs/video/moyu-intro.mp4).
    for src, name in ((VIDEO, "moyu-intro.mp4"), (VIDEO.with_name("moyu-intro-en.mp4"), "moyu-intro-en.mp4")):
        if src.exists():
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(src), "-vf", "scale=720:1280", "-c:v", "libx264", "-crf", "27",
                            "-preset", "slow", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", str(OUT / name)], check=True)
    poster = frame(2.2, 540)
    cards = []
    for (b0, b1, sec, title, caption, what, why, pit, t) in SHOTS:
        t = t if t is not None else (b0 + b1) / 2 * BEAT
        cards.append(dict(t0=b0 * BEAT, t1=b1 * BEAT, sec=sec, secname=SECTION[sec], title=title, caption=caption,
                          what=what, why=why, pit=pit, img=frame(t)))
    pairs = []
    for name, t in (("回弹 → 幕墙立面", 5 * BEAT), ("实机：叠楼 → 体块研究", 29 * BEAT), ("蜿蜒 → 景观平面", 50 * BEAT), ("雨燕 → 采光剖面", 60 * BEAT)):
        pairs.append(dict(name=name, a=frame(t - 1 / 60 - 0.0005, 300), b=frame(t + 0.004, 300)))
    data = dict(cards=cards, pairs=pairs, scores=SCORES, poster=poster)
    page = (Path(__file__).parent / "report_template.html").read_text(encoding="utf-8")
    page = page.replace("/*DATA*/null", json.dumps(data, ensure_ascii=False))
    (OUT / "index.html").write_text(page, encoding="utf-8")
    print("report:", OUT / "index.html", f"{(OUT / 'index.html').stat().st_size / 1e6:.1f} MB,",
          f"video {(OUT / 'moyu-intro.mp4').stat().st_size / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
