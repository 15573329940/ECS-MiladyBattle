 特别声明
仅供学习交流：本工程仅用于个人技术研究与学习，请勿随意外传，严禁用于任何商业用途。
致谢与版权：美术资源与核心架构思路参考自 Turbo Makes Games 教程。如涉及版权问题，请联系作者删除。
技术演示视频：https://www.bilibili.com/video/BV14CicBzEbg/?spm_id_from=333.1007.top_right_bar_window_history.content.click&vd_source=c1cc9ec3bade3bb5b7bf2209d5ea13d5

 核心技术要点
环境版本：Unity 6000.2.12f1。
注意：若使用其他版本，请务必关注 DOTS 与 Netcode for Entities 接口的变更。

架构：基于Unity DOTS (ECS, Burst, Job System)框架与Netcode for Entities高性能状态同步框架实现三千人同屏，目前项目还没发挥架构的全部实力，还能继续优化。
Scripts文件夹结构：
  Core：服务器创与客户端连接
  Gameplay：核心逻辑，包括相机，战斗，玩家逻辑，小兵逻辑，游戏循环。
  UI：OOP的UI和小地图部分。
  
逻辑流水线：
System的划分基本是按照游戏流程一步一个System，拿小兵战斗部分举例：DamageOnTriggerSystem触发伤害->ApplyDamageSystem处理伤害->FloatingTextRenderSystem显示伤害。
网络同步方面，和玩家输入相关的System放预测组，其他的放非预测组。
扩展建议：特别注意用Job System多线程与Burst标签时，不能访问托管堆。并且请尽量使用他们，因为他们是CPU方面性能提升的关键。
          
