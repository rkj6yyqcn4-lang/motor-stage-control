clc; clear; close all;

%% =========================================================
%  HK_USB_DAQ + 步进驱动器 单独控制 X 轴测试程序
%
%  默认接线：共阳极接法
%
%  采集卡 5VO   -> 驱动器 PUL+
%  采集卡 PWM1  -> 驱动器 PUL-
%
%  采集卡 5VO   -> 驱动器 DIR+
%  采集卡 OUT3  -> 驱动器 DIR-
%
%  ENA 先不接
%
%  运动逻辑：
%  OUT3 控制方向
%  PWM1 输出脉冲
%  PWM 频率决定速度
%  PWM 输出时间决定大概走多远
%% =========================================================

%% ===== 1. DLL 路径 =====
% 如果 dll 和 h 文件与本 m 文件在同一个文件夹，可以直接这样写
dllPath = 'USBDAQ_DLL_V12X64.dll';
hPath   = 'HKDAQ_MATLAB.h';

% 如果不在同一个文件夹，改成绝对路径，例如：
% dllPath = 'D:\HK_USB_DAQ\64位库\usb_card_V12.dll';
% hPath   = 'D:\HK_USB_DAQ\64位库\usb_card_V12_dll.h';

libName = 'HKDAQ';

%% ===== 2. X轴端口定义 =====
% PWM1 是采集卡 PWM 输出通道 1
X_PWM_CH = 1;    

% OUT3 对应 DoSetV12 的数字输出通道 2
% 因为：
% PWM1/OUT1 -> DO0
% PWM2/OUT2 -> DO1
% OUT3      -> DO2
X_DIR_DO = 2;    

%% ===== 3. 测试参数 =====
% 第一次建议低频
pwmFreq = 10000;        % PWM频率，单位 Hz。先用 100~300Hz
runDuty = 50;         % 运动时占空比 50%

% 共阳极接法下：
% 5VO -> PUL+
% PWM1 -> PUL-
% 停止时让 PWM1 固定高电平，光耦不导通
stopDuty = 100;       

moveTime =5.0;       % 每次运动时间，单位秒

% 方向设置
% true / false 分别代表 OUT3 两种电平状态
% 如果方向反了，改这个变量或者交换 true/false
dirForward = true;

% 是否真正执行运动
RUN_TEST = true;

%% ===== 4. 加载 DLL =====
if ~libisloaded(libName)
    loadlibrary(dllPath, hPath, 'alias', libName);
end

cleanupObj = onCleanup(@() safeClose(libName, X_PWM_CH, pwmFreq, stopDuty));

%% ===== 5. 打开采集卡 =====
ret = calllib(libName, 'OpenUsbV12');
if ret ~= 0
    error('OpenUsbV12 打开采集卡失败，返回值 = %d', ret);
end

disp('采集卡已打开');

%% ===== 6. 先停止 PWM，防止上电误动作 =====
setPWM(libName, X_PWM_CH, pwmFreq, stopDuty);
pause(0.2);

disp('PWM1 已停止');

%% ===== 7. X轴正方向运动 =====
if RUN_TEST
    disp('准备 X 轴正方向运动');

    % 设置方向
    setDO(libName, X_DIR_DO, dirForward);
    pause(0.05);   % 方向信号建立时间

    fprintf('X轴开始运动：频率 = %d Hz，占空比 = %.1f%%，时间 = %.3f s\n', ...
        pwmFreq, runDuty, moveTime);

    % 输出 PWM 脉冲
    setPWM(libName, X_PWM_CH, pwmFreq, runDuty);

    % 维持一段时间
    pause(moveTime);

    % 停止 PWM
    setPWM(libName, X_PWM_CH, pwmFreq, stopDuty);

    disp('X轴运动停止');

    pause(0.5);

    %% ===== 8. X轴反方向运动 =====
    
    disp('准备 X 轴反方向运动');

    setDO(libName, X_DIR_DO, ~dirForward);
    pause(0.05);

    fprintf('X轴反方向运动：频率 = %d Hz，占空比 = %.1f%%，时间 = %.3f s\n', ...
        pwmFreq, runDuty, moveTime);

    setPWM(libName, X_PWM_CH, pwmFreq, runDuty);
    pause(moveTime);
    setPWM(libName, X_PWM_CH, pwmFreq, stopDuty);

    disp('X轴反方向运动停止');
else
    disp('RUN_TEST = false，没有执行运动。');
end

%% ===== 9. 关闭设备 =====
safeClose(libName, X_PWM_CH, pwmFreq, stopDuty);
disp('程序结束');

%% =========================================================
%                    局部函数
%% =========================================================

function setPWM(libName, pwmChan, freq, duty)
    % PWMOutSetV12(int chan, int Freq, float DutyCycle)
    ret = calllib(libName, 'PWMOutSetV12', int32(pwmChan), int32(freq), single(duty));
    if ret ~= 0
        error('PWMOutSetV12 设置失败，PWM通道 = %d，返回值 = %d', pwmChan, ret);
    end
end

function setDO(libName, doChan, state)
    % DoSetV12(unsigned char chan, unsigned char state)
    if state
        s = uint8(1);
    else
        s = uint8(0);
    end

    ret = calllib(libName, 'DoSetV12', uint8(doChan), s);
    if ret ~= 0
        error('DoSetV12 设置失败，DO通道 = %d，返回值 = %d', doChan, ret);
    end
end

function safeClose(libName, pwmChan, freq, stopDuty)
    try
        if libisloaded(libName)
            calllib(libName, 'PWMOutSetV12', int32(pwmChan), int32(freq), single(stopDuty));
            calllib(libName, 'CloseUsbV12');
            unloadlibrary(libName);
        end
    catch
    end
end