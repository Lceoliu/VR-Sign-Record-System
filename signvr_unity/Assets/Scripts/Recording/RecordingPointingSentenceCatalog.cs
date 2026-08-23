namespace SignVR.Recording
{
    /// <summary>
    /// Canonical Unity-side authoring data for the 31 pointing recordings.
    /// The host catalog uses the same IDs; repeated prompt text is intentional.
    /// </summary>
    public static class RecordingPointingSentenceCatalog
    {
        public const int SentenceCount = 31;

        public static RecordingSentence[] CreateSentences()
        {
            return new[]
            {
                Entry(1, "找到那个箱子下面的密码", "state_01", "凳子上的箱子", "box"),
                Entry(2, "找到那个箱子下面的密码", "state_01", "地面箱子 A", "box (1)"),
                Entry(3, "找到那个箱子下面的密码", "state_01", "地面箱子 B", "box (2)"),

                Entry(4, "把这个金币放到那个盘子里面", "state_02", "龙纹金币 → 龙纹盘", "dragon_coin", "dragon_plate"),
                Entry(5, "把这个金币放到那个盘子里面", "state_02", "龙纹金币 → 盘子 A", "dragon_coin", "plate"),
                Entry(6, "把这个金币放到那个盘子里面", "state_02", "龙纹金币 → 盘子 B", "dragon_coin", "plate (1)"),
                Entry(7, "把这个金币放到那个盘子里面", "state_02", "金币 A → 龙纹盘", "golden_coin", "dragon_plate"),
                Entry(8, "把这个金币放到那个盘子里面", "state_02", "金币 A → 盘子 A", "golden_coin", "plate"),
                Entry(9, "把这个金币放到那个盘子里面", "state_02", "金币 A → 盘子 B", "golden_coin", "plate (1)"),
                Entry(10, "把这个金币放到那个盘子里面", "state_02", "金币 B → 龙纹盘", "golden_coin (1)", "dragon_plate"),
                Entry(11, "把这个金币放到那个盘子里面", "state_02", "金币 B → 盘子 A", "golden_coin (1)", "plate"),
                Entry(12, "把这个金币放到那个盘子里面", "state_02", "金币 B → 盘子 B", "golden_coin (1)", "plate (1)"),

                Entry(13, "把那个画框拿下来，找到它背后的密码", "state_03", "画框 A", "picture_frame"),
                Entry(14, "把那个画框拿下来，找到它背后的密码", "state_03", "画框 B", "fancy_picture_frame"),
                Entry(15, "把那个画框拿下来，找到它背后的密码", "state_03", "画框 C", "white_photo_frame"),

                Entry(16, "输入密码，打开箱子，拿起那把钥匙", "state_04", "钥匙 A", "chest/key"),
                Entry(17, "输入密码，打开箱子，拿起那把钥匙", "state_04", "钥匙 B", "chest/key (1)"),
                Entry(18, "输入密码，打开箱子，拿起那把钥匙", "state_04", "摩托车钥匙", "chest/motorbike_key"),

                Entry(19, "用钥匙打开柜子，按下那些按钮", "state_05", "按钮 A", "industrial_button"),
                Entry(20, "用钥匙打开柜子，按下那些按钮", "state_05", "按钮 B", "red_button"),
                Entry(21, "用钥匙打开柜子，按下那些按钮", "state_05", "按钮 C", "alarm_button"),
                Entry(22, "用钥匙打开柜子，按下那些按钮", "state_05", "按钮 A+B", "industrial_button", "red_button"),
                Entry(23, "用钥匙打开柜子，按下那些按钮", "state_05", "按钮 A+C", "industrial_button", "alarm_button"),
                Entry(24, "用钥匙打开柜子，按下那些按钮", "state_05", "按钮 B+C", "red_button", "alarm_button"),
                Entry(25, "用钥匙打开柜子，按下那些按钮", "state_05", "按钮 A+B+C", "industrial_button", "red_button", "alarm_button"),

                OrderedEntry(26, "电闸顺序 A→B→C", new[] { 1, 2, 3 }),
                OrderedEntry(27, "电闸顺序 A→C→B", new[] { 1, 3, 2 }),
                OrderedEntry(28, "电闸顺序 B→A→C", new[] { 2, 1, 3 }),
                OrderedEntry(29, "电闸顺序 B→C→A", new[] { 3, 1, 2 }),
                OrderedEntry(30, "电闸顺序 C→A→B", new[] { 2, 3, 1 }),
                OrderedEntry(31, "电闸顺序 C→B→A", new[] { 3, 2, 1 })
            };
        }

        private static RecordingSentence Entry(
            int number,
            string text,
            string viewpointId,
            string targetLabel,
            params string[] targetIds)
        {
            return new RecordingSentence(
                $"sentence_{number:D3}",
                text,
                viewpointId,
                targetLabel,
                targetIds
            );
        }

        private static RecordingSentence OrderedEntry(
            int number,
            string targetLabel,
            int[] sequenceNumbers)
        {
            return new RecordingSentence(
                $"sentence_{number:D3}",
                "按顺序依次拉下对应电闸",
                "state_06",
                targetLabel,
                new[]
                {
                    "free_switch_handler_ue5",
                    "free_switch_handler_ue5 (1)",
                    "free_switch_handler_ue5 (2)"
                },
                sequenceNumbers
            );
        }
    }
}
