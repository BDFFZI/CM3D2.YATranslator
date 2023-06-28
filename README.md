## 相对原版的修改
- 修复了多人语音下字幕丢失的问题。
- 对文本翻译文件的加载进行了优化。（[字符串是优化的大头](https://docs.unity.cn/cn/2019.4/Manual/BestPracticeUnderstandingPerformanceInUnity5.html)）
  - 改为使用Ordinal比较器的字符串字典，这点可以大幅提高代码执行效率。
  - 将一些字符串和文件的处理函数改用手工编码的版本，这比系统库函数的效率要高。
  - 字典的初始容量将以读取的首个文件大小来预测，这可以减少字典扩容的次数，对单一文件的情况更友好。
- 翻译音频时会在日志里打印音频名和译文。
- 加载完文本翻译文件后会在日志打印耗时。

注意：所有修改都只在CM3D2.YATranslator.Plugin.dll上发生，所以你只需要替换这一个DLL即可。

## 额外的一些帮助
- 建议将多个文本文件合并成一个，这能大幅提高加载速度。
- 添加了对卡拉OK模式字幕的支持（是通过音频翻译功能实现的，请下载右侧特定的releases包使用）。
