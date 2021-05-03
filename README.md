# Nova（能叶）

![Nova banner](https://github.com/Lunatic-Works/Nova/wiki/img/nova_banner.png)

基于Unity，对程序员友好的视觉小说（VN）/文字冒险游戏（AVG）框架

设计思路和一些特点的介绍：https://zhuanlan.zhihu.com/p/272466277

本框架支持Unity 2019及更高版本，Windows/Linux/macOS/Android/iOS平台

新功能将会随着我们的下一部作品一起开发，预计的功能包括URP，以及更稳定、更灵活的脚本

## 快速上手

1. `git clone`
2. 将`ProjectSettings/ProjectVersion.txt`中的Unity版本号改为你的版本号
3. 准备打开我们的示例工程
    * 将`Assets/Examples/Colorless/`文件夹下的`Prefabs/, Scenes/, StandingsUncropped/`和对应的`.meta`文件移动到`Assets/`文件夹下
    * 将`Assets/Examples/Colorless/Resources/`文件夹下的`Colorless/, Locales/`和对应的`.meta`文件移动到`Assets/Resources/`文件夹下
    * 如果操作正确，移动的过程中不会覆盖任何文件，`Assets/Examples/`文件夹下也不会剩下任何文件（除了空文件夹和对应的`.meta`文件）
4. 打开`Assets/Scenes/Main.unity`，体验示例作品
5. 在开始改脚本之前，先删除英文版的脚本，以免发生冲突
    * `Assets/Resources/Locales/English/Colorless/Scenarios/`文件夹下是英文版的脚本，可以全部删除
    * `Assets/Resources/LocalizedResourcePaths.txt`里面是英文版用到的文件，可以全部清空，但是这个文件本身不能删除
5. 试着改一改脚本吧，脚本放在`Assets/Resources/Colorless/Scenarios/`文件夹下

## 说明文档

GitHub Wiki（正在施工中）：https://github.com/Lunatic-Works/Nova/wiki/

## 友情链接

* 我们的第一部视觉小说作品《青箱》：[Steam页面](https://store.steampowered.com/app/1131740) [知乎页面](https://www.zhihu.com/question/409724349) [Bangumi页面](https://bgm.tv/subject/311066)
* 我们的微博：[@LunaticWorks](https://weibo.com/LunaticWorks)
* 我们的QQ群：876212259，如果以后讨论程序的人多了可能会再开一个程序群

也可以看一看其他的引擎：

* [Ren'Py](https://github.com/renpy/renpy)：如果你没有编程基础但是想学，或者有Python基础，推荐用这个引擎
* [AVG.js](https://github.com/avgjs/avg-core)：如果你信仰web前端，推荐用这个引擎
* [Librian](https://github.com/RimoChan/Librian)：Python后端和web前端混合的引擎
* [AVGPlus](https://github.com/avg-plus/avg.renderer)：另一个使用web前端的引擎，似乎得到了Xihe Animation的支持
* [Snowing](https://github.com/Strrationalism/Snowing)：用C++写的硬核引擎
* [UniGal](https://github.com/Uni-Gal/UniGal-Script)：为了解决各家自研引擎的碎片化问题，而定义的通用脚本格式

本框架的依赖：

* [tolua#](https://github.com/topameng/tolua)
* [Json.NET](https://github.com/JamesNK/Newtonsoft.Json)

国内镜像（随缘更新）：

* https://gitee.com/woctordho/Nova
